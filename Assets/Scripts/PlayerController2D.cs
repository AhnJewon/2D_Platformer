using UnityEngine;
using UnityEngine.InputSystem;

// Side-scrolling player controller for the rigged Player.
//   Move:  A/D or arrow keys (gamepad: left stick / d-pad) - running is the default move.
//   Walk:  hold Left Ctrl (gamepad: left shoulder) or set forceWalk (cutscenes / special areas).
//   Jump:  Space, W or Up (gamepad: South). Release early for a short hop.
//   Dash:  Shift (gamepad: right shoulder) - short burst forward, works on the ground and once in the air.
//   Crouch: hold Down on the ground (gamepad: stick/d-pad down) - lower collider, slow crouch walk. She stays
//          crouched under a ceiling until there is room to stand.
//   Slide: Down + Shift on the ground (gamepad: down + right shoulder), also straight from a crouch - low slide under
//          obstacles; the hand-drawn slide sprite is shown while sliding (SlideVisual). Jump cancels a slide when
//          there is room to stand. A slide ends in a crouch if Down is still held or the ceiling is too low.
//   Aim:   with PlayerCombat she faces the mouse cursor; moving away from it is a slower backpedal (Backpedal state,
//          crouch walk plays in reverse). Dash/slide go the way you are moving (or facing when standing still).
// Drives the Animator parameters: Speed, Jump, Grounded, VelY, AirVel, Dashing, Sliding, Crouching,
// Backpedal, MoveRate (backpedal clip speed), MoveDir (+1 / -1 crouch-walk direction).
[RequireComponent(typeof(Rigidbody2D), typeof(CapsuleCollider2D))]
public class PlayerController2D : MonoBehaviour
{
    [Header("Move")]
    public float walkSpeed = 2.2f;
    public float runSpeed = 5f;
    public float backpedalSpeed = 2.6f;   // moving away from the aim (= the Backpedal clip's foot speed)
    public float groundAccel = 40f;
    public float airAccel = 25f;

    [Header("Jump")]
    public float jumpHeight = 1.4f;       // world units
    public float timeToApex = 0.4f;       // seconds
    public float fallMultiplier = 1.6f;   // fall faster than rise
    public float shortHopMultiplier = 2.5f; // gravity while rising after the button was released
    public float coyoteTime = 0.1f;
    public float jumpBuffer = 0.1f;
    public float maxFallSpeed = 12f;

    [Header("Dash")]
    public float dashSpeed = 11f;
    public float dashTime = 0.2f;
    public float dashCooldown = 0.3f;
    public int airDashes = 1;

    [Header("Crouch")]
    public float crouchSpeed = 1.4f;      // = the crouch-walk clip's foot speed (PlayerDraftClips.CrouchWalkSpeed)
    public float crouchHeight = 1.75f;    // collider height while crouched (standing 2.55)

    [Header("Slide")]
    public float slideSpeed = 8f;
    public float slideMinSpeed = 3.5f;
    public float slideTime = 0.55f;
    public float slideHeight = 0.95f;     // collider height while sliding
    public float slideSpriteDelay = 0.04f; // rig drops into the sit for ~1 frame, then the slide art is shown

    [Header("References")]
    public Transform model;
    public float visualOffsetY = -0.035f;   // art vs collider: boots on the ground line               // flipped to face the move direction (defaults to the Animator's object)

    enum Mode { Normal, Dash, Slide }
    Mode mode;
    Rigidbody2D rb;
    CapsuleCollider2D col;
    Animator anim;
    SlideVisual slideVisual;
    Vector2 standSize, standOffset;
    float moveInput, lastGrounded = -1f, lastJumpPressed = -1f, lastDashPressed = -1f, takeoffSpeed, gravity;
    float modeTime, nextDash, facing = 1f, dashDir = 1f;
    PlayerCombat combat;
    int airDashesLeft;
    bool walkHeld, jumpHeld, downHeld, grounded, jumping, crouching;
    readonly RaycastHit2D[] hits = new RaycastHit2D[4];
    readonly Collider2D[] overlaps = new Collider2D[4];

    // optional scripted input (tests, cutscenes); null = read the devices
    public float? scriptedMove;
    public bool scriptedRun, scriptedJumpHeld, scriptedDown;
    [Tooltip("Walk instead of run (cutscenes, special areas)")] public bool forceWalk;

    public bool IsGrounded => grounded;
    public bool IsDashing => mode == Mode.Dash;
    public bool IsSliding => mode == Mode.Slide;
    public bool IsCrouching => crouching;
    public float Facing => facing;
    public bool IsBackpedaling => mode == Mode.Normal && Mathf.Abs(moveInput) > 0.01f && Mathf.Sign(moveInput) != facing;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<CapsuleCollider2D>();
        anim = GetComponentInChildren<Animator>();
        slideVisual = GetComponent<SlideVisual>();
        combat = GetComponent<PlayerCombat>();
        if (model == null && anim != null) model = anim.transform;
        // the drawn soles sit ~0.035 u above the rig's root: lower the art so the boots touch the ground
        if (model != null) model.localPosition += new Vector3(0f, visualOffsetY, 0f);
        rb.freezeRotation = true;
        rb.gravityScale = 0f; // gravity is applied here (asymmetric jump arc)
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        // no friction: otherwise pushing into a wall or platform side while jumping sticks the player to it
        if (col.sharedMaterial == null) col.sharedMaterial = new PhysicsMaterial2D("PlayerNoFriction") { friction = 0f, bounciness = 0f };
        standSize = col.size; standOffset = col.offset;
    }

    void Update()
    {
        gravity = 2f * jumpHeight / (timeToApex * timeToApex);
        takeoffSpeed = gravity * timeToApex;

        var kb = Keyboard.current; var pad = Gamepad.current;
        float x = 0f; bool jumpDown = false, dashDown = false;
        downHeld = false;
        if (kb != null)
        {
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) x -= 1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) x += 1f;
            walkHeld = kb.leftCtrlKey.isPressed;
            downHeld = kb.sKey.isPressed || kb.downArrowKey.isPressed;
            jumpDown = kb.spaceKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame;
            jumpHeld = kb.spaceKey.isPressed || kb.wKey.isPressed || kb.upArrowKey.isPressed;
            dashDown = kb.leftShiftKey.wasPressedThisFrame || kb.rightShiftKey.wasPressedThisFrame;
        }
        if (pad != null)
        {
            float px = pad.leftStick.x.ReadValue() + pad.dpad.x.ReadValue();
            if (Mathf.Abs(px) > 0.2f) x = Mathf.Clamp(px, -1f, 1f);
            walkHeld |= pad.leftShoulder.isPressed;
            downHeld |= pad.leftStick.y.ReadValue() < -0.5f || pad.dpad.down.isPressed;
            jumpDown |= pad.buttonSouth.wasPressedThisFrame;
            jumpHeld |= pad.buttonSouth.isPressed;
            dashDown |= pad.rightShoulder.wasPressedThisFrame;
        }
        if (scriptedMove.HasValue) { x = scriptedMove.Value; walkHeld = !scriptedRun; jumpHeld = scriptedJumpHeld; downHeld = scriptedDown; }
        if (IsStunned) { x = 0f; jumpDown = false; dashDown = false; downHeld = false; }   // knocked back: no control
        moveInput = x;
        if (jumpDown) lastJumpPressed = Time.time;
        if (dashDown) lastDashPressed = Time.time;

        // face the aim (PlayerCombat) or else the move direction; a dash or slide faces the way it goes
        if (mode == Mode.Normal)
        {
            float aim = combat != null ? combat.DesiredFacing() : 0f;
            if (aim != 0f) facing = aim;
            else if ((combat == null || !combat.isActiveAndEnabled) && Mathf.Abs(x) > 0.01f) facing = Mathf.Sign(x);
        }
        else facing = dashDir;
        if (model != null) { var s = model.localScale; s.x = Mathf.Abs(s.x) * facing; model.localScale = s; }

        if (anim != null)
        {
            // an attack step in place (PlayerCombat.Lunge) moves her a little but keeps the stand-still animations
            bool stepOnly = Time.time < attackStepUntil && Mathf.Abs(moveInput) < 0.01f;
            anim.SetFloat("Speed", stepOnly ? 0f : Mathf.Abs(rb.linearVelocity.x));
            anim.SetBool("Grounded", grounded);
            anim.SetFloat("VelY", rb.linearVelocity.y);
            anim.SetFloat("AirVel", Mathf.Clamp(rb.linearVelocity.y / Mathf.Max(takeoffSpeed, 0.01f), -1f, 1f));
            anim.SetBool("Dashing", mode == Mode.Dash);
            anim.SetBool("Sliding", mode == Mode.Slide);
            anim.SetBool("Crouching", crouching);
            float vx = rb.linearVelocity.x;
            bool backward = Mathf.Abs(vx) > 0.05f && Mathf.Sign(vx) != facing;
            anim.SetBool("Backpedal", mode == Mode.Normal && backward && !crouching && Mathf.Abs(vx) > 0.3f && !stepOnly);
            anim.SetFloat("MoveRate", Mathf.Clamp(Mathf.Abs(vx) / backpedalSpeed, 0.3f, 1.5f));
            anim.SetFloat("MoveDir", backward ? -1f : 1f);
        }
        if (slideVisual) slideVisual.Show(mode == Mode.Slide && modeTime >= slideSpriteDelay);
    }

    // scripted presses (tests)
    public void PressJump(bool hold = true) { lastJumpPressed = Time.time; jumpHeld = hold; scriptedJumpHeld = hold; }
    public void PressDash() { lastDashPressed = Time.time; }

    // Hit: knocked back with `velocity` and no control for `seconds` (a dash or slide is cut short; she stands up).
    float stunUntil = -10f;
    public bool IsStunned => Time.time < stunUntil;
    public void Knockback(Vector2 velocity, float seconds)
    {
        if (mode == Mode.Dash) { mode = Mode.Normal; nextDash = Time.time + dashCooldown; }
        if (mode == Mode.Slide && !CeilingBlocked(standSize.y)) EndSlide();
        stunUntil = Time.time + seconds;
        rb.linearVelocity = velocity;
        if (velocity.y > 0.1f) { grounded = false; jumping = true; lastGrounded = -1f; }
        lastJumpPressed = lastDashPressed = -1f;
    }

    // attack step (PlayerCombat): on the ground a short push toward the aim (bleeds off with groundAccel); in the air a
    // brief hang - the fall is stopped once per jump, like the air slashes in most action platformers.
    public void Lunge(Vector2 dir, float groundSpeed, float airHang)
    {
        if (mode != Mode.Normal) return;
        var v = rb.linearVelocity;
        attackStepUntil = Time.time + 0.3f;
        if (grounded) { if (!crouching) v.x += Mathf.Sign(dir.x) * groundSpeed * Mathf.Clamp01(Mathf.Abs(dir.x) * 1.5f); }
        else if (!airLungeUsed) { airLungeUsed = true; v.y = Mathf.Max(v.y, airHang); }
        rb.linearVelocity = v;
    }
    bool airLungeUsed;
    float attackStepUntil;
    // a push that is not walking (gun kickback): keeps the stand-still animations for a moment
    public void MarkStepInPlace(float seconds = 0.25f) { attackStepUntil = Mathf.Max(attackStepUntil, Time.time + seconds); }
    public bool IsAttackStep => Time.time < attackStepUntil && Mathf.Abs(moveInput) < 0.01f && grounded && !crouching;

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        grounded = CheckGround();
        if (grounded) { lastGrounded = Time.time; airDashesLeft = airDashes; airLungeUsed = false;if (rb.linearVelocity.y <= 0.01f) jumping = false; }
        var v = rb.linearVelocity;
        modeTime += dt;

        // dash / slide requests
        if (mode == Mode.Normal && Time.time - lastDashPressed <= jumpBuffer && Time.time >= nextDash)
        {
            dashDir = Mathf.Abs(moveInput) > 0.01f ? Mathf.Sign(moveInput) : facing;
            if (grounded && (downHeld || crouching)) { StartSlide(); }
            else if (grounded || airDashesLeft > 0)
            {
                if (!grounded) airDashesLeft--;
                mode = Mode.Dash; modeTime = 0f; lastDashPressed = -1f;
            }
        }

        if (mode == Mode.Dash)
        {
            v = new Vector2(dashDir * dashSpeed, 0f);               // straight line, no gravity
            if (modeTime >= dashTime)
            {
                mode = Mode.Normal; nextDash = Time.time + dashCooldown;
                v.x = Mathf.Abs(moveInput) > 0.01f ? Mathf.Sign(moveInput) * (Mathf.Sign(moveInput) == dashDir ? runSpeed : backpedalSpeed) : 0f;
            }
            rb.linearVelocity = v;
            return;
        }

        if (mode == Mode.Slide)
        {
            float t = Mathf.Clamp01(modeTime / slideTime);
            v.x = dashDir * Mathf.Lerp(slideSpeed, slideMinSpeed, t);
            bool blocked = CeilingBlocked(standSize.y);         // no room to stand
            bool lowBlocked = CeilingBlocked(crouchHeight);     // no room even to crouch: keep sliding
            bool jumpReq = Time.time - lastJumpPressed <= jumpBuffer;
            if (!grounded && !lowBlocked) EndSlide();               // slid off a ledge
            else if (jumpReq && !blocked) { EndSlide(); }           // jump cancels the slide (handled below)
            else if (modeTime >= slideTime && !lowBlocked)
            {
                EndSlide();
                if (grounded && (downHeld || blocked)) SetCrouch(true);   // slide -> crouch
                v.x = Mathf.Abs(moveInput) > 0.01f ? Mathf.Sign(moveInput) * (crouching ? crouchSpeed : runSpeed) : 0f;
            }
            if (mode == Mode.Slide)
            {
                v.y = grounded ? 0f : v.y - gravity * fallMultiplier * dt;
                rb.linearVelocity = v;
                return;
            }
        }

        // crouch while Down is held on the ground; stand up only when there is room
        if (grounded && downHeld && !crouching) SetCrouch(true);
        else if (crouching && (!downHeld || !grounded) && !CeilingBlocked(standSize.y)) SetCrouch(false);

        bool backpedal = Mathf.Abs(moveInput) > 0.01f && Mathf.Sign(moveInput) != facing;
        float speed = crouching ? crouchSpeed : walkHeld || forceWalk ? walkSpeed : runSpeed;
        if (backpedal && !crouching) speed = Mathf.Min(speed, backpedalSpeed);
        float target = moveInput * speed;
        float accel = IsStunned ? (grounded ? 8f : 2f) : grounded ? groundAccel : airAccel;   // knockback slides out
        v.x = Mathf.MoveTowards(v.x, target, accel * dt);

        // jump with coyote time and input buffer
        bool canJump = !jumping && Time.time - lastGrounded <= coyoteTime && !(crouching && CeilingBlocked(standSize.y));
        if (canJump && Time.time - lastJumpPressed <= jumpBuffer)
        {
            v.y = takeoffSpeed;
            jumping = true; grounded = false; lastJumpPressed = -1f; lastGrounded = -1f;
            if (crouching) SetCrouch(false);
            if (anim != null) anim.SetTrigger("Jump");
        }

        // gravity: normal while rising with the button held, stronger after release (short hop), stronger when falling
        float g = gravity;
        if (v.y < 0f) g *= fallMultiplier;
        else if (!jumpHeld) g *= shortHopMultiplier;
        if (!grounded || v.y > 0f) v.y -= g * dt;
        v.y = Mathf.Max(v.y, -maxFallSpeed);
        rb.linearVelocity = v;
    }

    // collider keeps its bottom (feet) and changes height
    void SetHeight(float h)
    {
        float bottom = standOffset.y - standSize.y / 2f;
        col.size = new Vector2(standSize.x, h);
        col.offset = new Vector2(standOffset.x, bottom + h / 2f);
    }

    void SetCrouch(bool on)
    {
        crouching = on;
        if (mode == Mode.Normal) { if (on) SetHeight(crouchHeight); else { col.size = standSize; col.offset = standOffset; } }
    }

    void StartSlide()
    {
        mode = Mode.Slide; modeTime = 0f; lastDashPressed = -1f;
        crouching = false;
        SetHeight(slideHeight);
    }

    void EndSlide()
    {
        mode = Mode.Normal; nextDash = Time.time + dashCooldown;
        col.size = standSize; col.offset = standOffset;
    }

    // is there something above the current collider, below `height` from the feet?
    bool CeilingBlocked(float height)
    {
        var b = col.bounds;
        float h = height - b.size.y;
        if (h <= 0.001f) return false;
        var center = new Vector2(b.center.x, b.max.y + h / 2f + 0.02f);
        int n = Physics2D.OverlapBox(center, new Vector2(b.size.x * 0.9f, h), 0f, ContactFilter2D.noFilter, overlaps);
        for (int i = 0; i < n; ++i) if (overlaps[i] != col && !overlaps[i].isTrigger) return true;
        return false;
    }

    bool CheckGround()
    {
        var b = col.bounds;
        var size = new Vector2(b.size.x * 0.9f, 0.05f);
        var origin = new Vector2(b.center.x, b.min.y + 0.03f);
        int n = Physics2D.BoxCast(origin, size, 0f, Vector2.down, ContactFilter2D.noFilter, hits, 0.08f);
        for (int i = 0; i < n; ++i)
            if (hits[i].collider != col && !hits[i].collider.isTrigger && hits[i].normal.y > 0.6f) return true;
        return false;
    }
}
