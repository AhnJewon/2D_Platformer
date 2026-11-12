using UnityEngine;
using UnityEngine.InputSystem;

// Side-scrolling player controller for the rigged Player.
//   Move:  A/D or arrow keys (gamepad: left stick / d-pad) - running is the default move.
//   Walk:  hold Left Ctrl (gamepad: left shoulder) or set forceWalk (cutscenes / special areas).
//   Jump:  Space, W or Up (gamepad: South). Release early for a short hop.
//   Dash:  Shift (gamepad: right shoulder) - short burst forward, works on the ground and once in the air.
//   Slide: Down + Shift on the ground (gamepad: down + right shoulder) - low slide under obstacles; the hand-drawn
//          slide sprite is shown while sliding (SlideVisual). Jump cancels a slide when there is room to stand.
// Drives the Animator parameters: Speed, Jump, Grounded, VelY, AirVel, Dashing, Sliding.
[RequireComponent(typeof(Rigidbody2D), typeof(CapsuleCollider2D))]
public class PlayerController2D : MonoBehaviour
{
    [Header("Move")]
    public float walkSpeed = 2.2f;
    public float runSpeed = 5f;
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

    [Header("Slide")]
    public float slideSpeed = 8f;
    public float slideMinSpeed = 3.5f;
    public float slideTime = 0.55f;
    public float slideHeight = 0.95f;     // collider height while sliding
    public float slideSpriteDelay = 0.04f; // rig drops into the sit for ~1 frame, then the slide art is shown

    [Header("References")]
    public Transform model;               // flipped to face the move direction (defaults to the Animator's object)

    enum Mode { Normal, Dash, Slide }
    Mode mode;
    Rigidbody2D rb;
    CapsuleCollider2D col;
    Animator anim;
    SlideVisual slideVisual;
    Vector2 standSize, standOffset;
    float moveInput, lastGrounded = -1f, lastJumpPressed = -1f, lastDashPressed = -1f, takeoffSpeed, gravity;
    float modeTime, nextDash, facing = 1f;
    int airDashesLeft;
    bool walkHeld, jumpHeld, downHeld, grounded, jumping;
    readonly RaycastHit2D[] hits = new RaycastHit2D[4];
    readonly Collider2D[] overlaps = new Collider2D[4];

    // optional scripted input (tests, cutscenes); null = read the devices
    public float? scriptedMove;
    public bool scriptedRun, scriptedJumpHeld, scriptedDown;
    [Tooltip("Walk instead of run (cutscenes, special areas)")] public bool forceWalk;

    public bool IsGrounded => grounded;
    public bool IsDashing => mode == Mode.Dash;
    public bool IsSliding => mode == Mode.Slide;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<CapsuleCollider2D>();
        anim = GetComponentInChildren<Animator>();
        slideVisual = GetComponent<SlideVisual>();
        if (model == null && anim != null) model = anim.transform;
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
        moveInput = x;
        if (jumpDown) lastJumpPressed = Time.time;
        if (dashDown) lastDashPressed = Time.time;

        // face the move direction (not in the middle of a dash or slide)
        if (mode == Mode.Normal && Mathf.Abs(x) > 0.01f) facing = Mathf.Sign(x);
        if (model != null) { var s = model.localScale; s.x = Mathf.Abs(s.x) * facing; model.localScale = s; }

        if (anim != null)
        {
            anim.SetFloat("Speed", Mathf.Abs(rb.linearVelocity.x));
            anim.SetBool("Grounded", grounded);
            anim.SetFloat("VelY", rb.linearVelocity.y);
            anim.SetFloat("AirVel", Mathf.Clamp(rb.linearVelocity.y / Mathf.Max(takeoffSpeed, 0.01f), -1f, 1f));
            anim.SetBool("Dashing", mode == Mode.Dash);
            anim.SetBool("Sliding", mode == Mode.Slide);
        }
        if (slideVisual) slideVisual.Show(mode == Mode.Slide && modeTime >= slideSpriteDelay);
    }

    // scripted presses (tests)
    public void PressJump(bool hold = true) { lastJumpPressed = Time.time; jumpHeld = hold; scriptedJumpHeld = hold; }
    public void PressDash() { lastDashPressed = Time.time; }

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        grounded = CheckGround();
        if (grounded) { lastGrounded = Time.time; airDashesLeft = airDashes; if (rb.linearVelocity.y <= 0.01f) jumping = false; }
        var v = rb.linearVelocity;
        modeTime += dt;

        // dash / slide requests
        if (mode == Mode.Normal && Time.time - lastDashPressed <= jumpBuffer && Time.time >= nextDash)
        {
            if (grounded && downHeld) { StartSlide(); }
            else if (grounded || airDashesLeft > 0)
            {
                if (!grounded) airDashesLeft--;
                mode = Mode.Dash; modeTime = 0f; lastDashPressed = -1f;
            }
        }

        if (mode == Mode.Dash)
        {
            v = new Vector2(facing * dashSpeed, 0f);               // straight line, no gravity
            if (modeTime >= dashTime)
            {
                mode = Mode.Normal; nextDash = Time.time + dashCooldown;
                v.x = facing * Mathf.Max(Mathf.Abs(moveInput) > 0.01f ? runSpeed : 0f, 0f);
            }
            rb.linearVelocity = v;
            return;
        }

        if (mode == Mode.Slide)
        {
            float t = Mathf.Clamp01(modeTime / slideTime);
            v.x = facing * Mathf.Lerp(slideSpeed, slideMinSpeed, t);
            bool blocked = CeilingBlocked();
            bool jumpReq = Time.time - lastJumpPressed <= jumpBuffer;
            if (!grounded && !blocked) EndSlide();                  // slid off a ledge
            else if (jumpReq && !blocked) { EndSlide(); }           // jump cancels the slide (handled below)
            else if (modeTime >= slideTime && !blocked) { EndSlide(); v.x = facing * (Mathf.Abs(moveInput) > 0.01f ? runSpeed : 0f); }
            if (mode == Mode.Slide)
            {
                v.y = grounded ? 0f : v.y - gravity * fallMultiplier * dt;
                rb.linearVelocity = v;
                return;
            }
        }

        float target = moveInput * (walkHeld || forceWalk ? walkSpeed : runSpeed);
        float accel = grounded ? groundAccel : airAccel;
        v.x = Mathf.MoveTowards(v.x, target, accel * dt);

        // jump with coyote time and input buffer
        bool canJump = !jumping && Time.time - lastGrounded <= coyoteTime;
        if (canJump && Time.time - lastJumpPressed <= jumpBuffer)
        {
            v.y = takeoffSpeed;
            jumping = true; grounded = false; lastJumpPressed = -1f; lastGrounded = -1f;
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

    void StartSlide()
    {
        mode = Mode.Slide; modeTime = 0f; lastDashPressed = -1f;
        float bottom = standOffset.y - standSize.y / 2f;
        col.size = new Vector2(standSize.x, slideHeight);
        col.offset = new Vector2(standOffset.x, bottom + slideHeight / 2f);
    }

    void EndSlide()
    {
        mode = Mode.Normal; nextDash = Time.time + dashCooldown;
        col.size = standSize; col.offset = standOffset;
    }

    // is there something above the slide collider that would not let her stand up?
    bool CeilingBlocked()
    {
        var b = col.bounds;
        float h = standSize.y - slideHeight;
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
            if (hits[i].collider != col && hits[i].normal.y > 0.6f) return true;
        return false;
    }
}
