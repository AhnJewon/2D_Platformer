using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// Anything a weapon can hit (training dummy, enemies).
public interface IDamageable { void TakeHit(float damage, Vector2 dir, Vector2 point); }

// Mouse-aimed melee:
//   - She always faces the cursor (PlayerController2D asks DesiredFacing); moving away from it is a backpedal.
//   - The near arm holds the sabre and points it at the cursor; it is posed on top of the Animator every frame
//     (2-bone IK on arm_near_upper / arm_near_fore), so every body animation keeps playing underneath.
//   - Left click (hold to repeat) plays the weapon's combo (MeleeWeapon asset: sabre = cut -> thrust -> rising cut).
//     Clicking during a step or within the weapon's comboWindow after it continues the combo; otherwise it restarts.
// A swing is a whole-body move (ArtSource/references/combat_backpedal_reference.md):
//   wind-up  (pull the blade back further, coil the chest and pelvis away from the cut, far arm forward)
//   strike   (fast sweep past the aim line; chest and pelvis turn into the cut, arm fully extended, far arm swings
//             back to balance, short lunge step toward the aim - in the air a brief hang instead)
//   follow-through (blade carries on past the opposite rest angle, then settles; body unwinds)
// Hits land mid-strike; the slash trail shows only during the strike and fades over the follow-through.
//   Gamepad: right stick aims, right trigger attacks.
// Two weapon slots: 1 = melee (weaponData), 2 = gun (gunData, RangedWeapon). Switch with 1 / 2, Q, the mouse wheel or
// the gamepad's north button. With the gun the arm holds it out straight at the cursor (one-handed bullseye stance);
// left click fires (hold for automatic), R reloads (also automatic when empty). Each shot: bolt, muzzle flash, recoil
// (muzzle up, arm pulled back), a little screen shake and kickback.
[DefaultExecutionOrder(-50)]   // LateUpdate after the Animator but before SpriteSkin deformation (order 10)
[RequireComponent(typeof(PlayerController2D))]
public class PlayerCombat : MonoBehaviour
{
    [Header("Weapon")]
    public MeleeWeapon weaponData;           // art, grip and combo; without it the built-in sabre set is used
    public RangedWeapon gunData;             // slot 2
    public int slot;                         // 0 = melee, 1 = gun
    public bool HasGun => slot == 1 && gunData != null;
    public int Ammo { get; private set; }
    public bool Reloading => Time.time < reloadEnd;
    public int ShotCount { get; private set; }
    float nextShot, lastShot = -10f, reloadEnd = -10f, reloadStart = -10f;
    public RangedWeapon.Stance Stance => stanceOverride ?? (gunData != null ? gunData.stance : RangedWeapon.Stance.OneHand);
    RangedWeapon.Stance? stanceOverride;
    public void CycleStance() => stanceOverride = (RangedWeapon.Stance)(((int)Stance + 1) % 3);
    bool TwoHanded => HasGun && Stance != RangedWeapon.Stance.OneHand;
    float RecoilScale => TwoHanded ? gunData.twoHandRecoil : 1f;
    Transform farHand;
    bool reloadPending, reloadFromEmpty;
    public float emptyReloadDelay = 0.3f;
    public void SetAmmoForTest(int n) { Ammo = n; reloadPending = false; reloadEnd = -10f; }
    GunCellGauge gauge;
    public float ReloadProgress => reloadPending ? Mathf.Clamp01((Time.time - reloadStart) / Mathf.Max(gunData.reloadTime, 1e-3f)) : -1f;
    Sprite meleeSprite; float meleeGripOffset; Vector2 meleeGripLocal;
    public Sprite weaponSprite;
    public Sprite slashSprite;
    public int weaponSortingOrder = 26;      // under the near hand (27) so the grip sits in the fist
    public bool flipWeapon = false;          // mirror the sprite if its knuckle bow is drawn below the grip (it must sit on the finger side)
    public Color slashColor = new Color(1f, 1f, 1f, 1f);

    [Header("Attack")]
    public float damage = 10f;
    public float swingTime = 0.3f;           // wind-up + strike + follow-through
    public float windUp = 0.045f;            // s, short so the click still feels instant
    public float strike = 0.075f;            // s, the fast part of the sweep
    public float attackInterval = 0.32f;
    public float restAngle = 70f;            // high guard: blade this far above the aim line
    public float restLow = 30f;              // low guard: blade this far below it (cut ends forward-low, as in Mega Man Zero 3)
    public float windUpExtra = 25f;          // pulled back this much further before the strike
    public float overshoot = 10f;            // carries this far past the opposite guard
    public float reach = 2.1f;               // from the shoulder (arm + blade tip ~2.3 u)
    public float arcHalf = 80f;              // hit arc either side of the aim
    public float knockback = 5f;
    public float hitStop = 0.05f;            // real seconds
    public float lungeSpeed = 3.5f;          // ground step toward the aim (u/s, bleeds off with the controller's accel)
    public float airHang = 1.5f;             // in the air: vertical speed floor at the strike (once per jump)

    [Header("Body")]
    public float armFollow = 0.4f;           // arm leans this much of the blade offset toward the blade (then clamped)
    // The sabre is rigid in the fist: blade angle = forearm angle + wrist. In the drawn fist (bind pose, arm hanging)
    // the blade points straight forward, i.e. 90 deg from the forearm; the wrist can bend it to 15..110 deg.
    public float bladeToForearmMin = 40f;    // wrist kept within ~50 deg of neutral (90): the arm does the swing
    public float bladeToForearmMax = 125f;
    // References: ArtSource/references/combat_backpedal_reference.md (game sword attacks, kendo men-strike posture, sabre
    // lunge. The trunk stays STRAIGHT - lean comes from the pelvis, never a bend at the waist; the head stays level.
    public float pelvisLean = 12f;           // trunk lean into the strike (from the hips)
    public float pelvisRise = 4f;            // slight rise/lean back in the wind-up
    public float lungeStride = 0.5f;         // front foot steps this far forward (attack in place on the ground)
    public float lungeBack = 0.35f;          // rear foot slides this far back
    public float lungeDrop = 0.22f;          // hips drop (front knee bends, rear leg nearly straight)
    public bool freeHandPoses = false;       // swap the free hand to open / spread drawings (HandPoses); off = rig fist
    public float freeHandOnHip = 1f;         // free hand goes to the hip while attacking (0 = leave the animation)

    [Header("Aim")]
    public float facingDeadZone = 0.12f;     // cursor this close to the body (x) keeps the current facing
    public float armReach = 0.62f;           // shoulder -> wrist while aiming (arm is ~0.75 long)

    // scripted aim/attack (tests)
    public Vector2? scriptedAim;
    public void PressAttack() => attackQueued = true;
    // tests: make the next click play combo step i (0-based)
    public void ForceStep(int i) { comboIndex = i - 1; lastEnd = stepEnd = Time.time; nextAttack = 0f; attackQueued = true; }

    public float AimAngle { get; private set; }      // model space, degrees (0 = straight ahead)
    public bool IsSwinging => Time.time < stepEnd;
    public int ComboIndex => comboIndex;
    public string CurrentStep => cur != null ? cur.name : "";
    MeleeWeapon.Step[] Steps => weaponData != null && weaponData.combo != null && weaponData.combo.Length > 0 ? weaponData.combo : defaultSteps;
    float GuardHigh => weaponData != null ? weaponData.guardHigh : restAngle;
    float ComboWindow => weaponData != null ? weaponData.comboWindow : 0.4f;
    static MeleeWeapon.Step[] defaultSteps;
    MeleeWeapon.Step cur;
    int comboIndex = -1;
    float stepEnd = -10f, fromAngle, lastRel, lastEnd = -10f;
    public int SwingCount { get; private set; }
    public int HitCount { get; private set; }
    public Vector2 LastHitDir { get; private set; }

    PlayerController2D pc;
    Transform model, upper, fore, hand, head, neck, spine, hip, farUpper, farFore, legRear, legFront, shinRear, shinFront, footRear, footFront;
    float stance;   // 0..1 lunge stance weight (smoothed)
    float bladeToHand;   // blade angle - hand bone angle (fixed grip)
    Vector3 fistLocal;   // fist centre in the hand bone's space
    Transform toeRear, toeFront;
    float guard;         // 0..1: attacking (free hand to the hip), smoothed
    bool planted; Vector3 rearToeW, frontAnkleW;   // lunge: feet fixed in the world during the step
    float gripInline;    // 0..1: arm reaching along the blade (in-line grip drawing)
    bool usingInline;
    HandPoses hands;
    Rigidbody2D rb;
    public float inlineGripDist = 0.17f;
    public bool useInlineGrip = false;      // the v4.5 in-line grip drawing showed the finger side: off
    public float gripBladeOffset = 77.4f;   // "Grip" drawing: hilt angle from the wrist->knuckle axis (Tools/import_hands.py)
    public Vector2 gripLocal = new Vector2(0.158f, -0.019f);   // "Grip" drawing: hilt centre from the wrist, in the hand frame   // wrist -> sword grip in the in-line grip drawing (Tools/import_hands.py)
    Transform weapon; SpriteRenderer weaponSr;
    Camera cam;
    Vector2 aimWorld; bool attackQueued, lungeDone;
    float side = 1f, swingStart = -10f, nextAttack;
    bool hitChecked;
    readonly Collider2D[] overlaps = new Collider2D[16];
    readonly HashSet<IDamageable> hitThisSwing = new HashSet<IDamageable>();

    void Awake()
    {
        pc = GetComponent<PlayerController2D>();
        hands = GetComponent<HandPoses>();
        if (defaultSteps == null) defaultSteps = MeleeWeapon.DefaultSabre();
        if (weaponData != null)
        {
            if (weaponData.sprite != null) weaponSprite = weaponData.sprite;
            if (weaponData.slashSprite != null) slashSprite = weaponData.slashSprite;
            gripBladeOffset = weaponData.gripBladeOffset; gripLocal = weaponData.gripLocal;
        }
        lastRel = restAngle;
        meleeGripOffset = gripBladeOffset; meleeGripLocal = gripLocal;
        if (gunData != null) Ammo = gunData.magazine;
        rb = GetComponent<Rigidbody2D>();
        var anim = GetComponentInChildren<Animator>();
        model = pc.model != null ? pc.model : anim.transform;
        foreach (var t in GetComponentsInChildren<Transform>(true))
            switch (t.name)
            {
                case "arm_near_upper": upper = t; break;
                case "arm_near_fore": fore = t; break;
                case "arm_near_hand": hand = t; break;
                case "arm_far_upper": farUpper = t; break;
                case "arm_far_fore": farFore = t; break;
                case "arm_far_hand": farHand = t; break;
                case "head": head = t; break;
                case "neck": neck = t; break;
                case "spine": spine = t; break;
                case "hip": hip = t; break;
                case "leg_rear_upper": legRear = t; break;
                case "leg_front_upper": legFront = t; break;
                case "leg_rear_lower": shinRear = t; break;
                case "leg_front_lower": shinFront = t; break;
                case "foot_rear": footRear = t; break;
                case "foot_front": footFront = t; break;
                case "toe_rear": toeRear = t; break;
                case "toe_front": toeFront = t; break;
            }
        // bind pose (before any animation): blade straight forward (0 deg) in the hanging fist
        if (hand != null)
        {
            bladeToHand = 0f - HandDir();
            var fist = transform.GetComponentsInChildren<SpriteRenderer>(true);
            foreach (var r in fist) if (r.name == "13_hand_near") { fistLocal = hand.InverseTransformPoint(r.bounds.center); break; }
        }
        var w = new GameObject("Weapon"); weapon = w.transform; weapon.SetParent(model, false);
        weaponSr = w.AddComponent<SpriteRenderer>(); weaponSr.sprite = weaponSprite; weaponSr.sortingOrder = weaponSortingOrder;
        // the knuckle bow (and the cutting edge) must sit on the FINGER side of the fist; the drawn fist curls its fingers the
        // other way from the source image, so mirror the sabre across its blade axis
        weaponSr.flipY = flipWeapon;
        aimWorld = (Vector2)transform.position + new Vector2(3f, 1.6f);
        meleeSprite = weaponSprite;
        SetSlot(slot);
    }

    public void SetSlot(int s)
    {
        slot = s == 1 && gunData != null ? 1 : 0;
        if (weaponSr == null) return;
        if (slot == 1)
        {
            weaponSr.sprite = gunData.sprite; weaponSr.flipY = false;
            if (gauge == null && gunData.sprite != null)
            {
                gauge = weapon.gameObject.AddComponent<GunCellGauge>();
                // vent: the slots at the rear top of the slide (source px ~(330, 190), pivot (250, 560))
                float ppu = gunData.sprite.pixelsPerUnit;
                gauge.Init(weaponSr, gunData.sprite, new[] { new Vector2(80f / ppu, 370f / ppu), gunData.muzzle });
            }
            gripBladeOffset = gunData.gripBladeOffset; gripLocal = gunData.gripLocal;
        }
        else
        {
            if (gauge != null) gauge.Release(meleeSprite);
            weaponSr.sprite = meleeSprite; weaponSr.flipY = flipWeapon;
            gripBladeOffset = meleeGripOffset; gripLocal = meleeGripLocal;
        }
        stepEnd = Mathf.Min(stepEnd, Time.time); attackQueued = false;
    }

    public void Reload()
    {
        if (!HasGun || Reloading || Ammo >= gunData.magazine) return;
        reloadStart = Time.time; reloadEnd = Time.time + gunData.reloadTime; reloadPending = true; reloadFromEmpty = Ammo <= 0;
    }

    Vector2 ShoulderWorld => upper != null ? (Vector2)upper.position : (Vector2)transform.position + Vector2.up * 2f;

    // +1 / -1 = face right / left, 0 = keep the current facing (cursor right above/below her)
    public float DesiredFacing()
    {
        if (!isActiveAndEnabled) return 0f;
        float dx = aimWorld.x - transform.position.x;
        return Mathf.Abs(dx) < facingDeadZone ? 0f : Mathf.Sign(dx);
    }

    void Update()
    {
        if (cam == null) cam = Camera.main;
        var mouse = Mouse.current; var pad = Gamepad.current;
        bool attackHeld = false;
        if (scriptedAim.HasValue) aimWorld = scriptedAim.Value;
        else
        {
            if (mouse != null && cam != null)
            {
                Vector3 sp = mouse.position.ReadValue(); sp.z = -cam.transform.position.z;
                aimWorld = cam.ScreenToWorldPoint(sp);
                attackHeld |= mouse.leftButton.isPressed;
            }
            if (pad != null)
            {
                var rs = pad.rightStick.ReadValue();
                if (rs.magnitude > 0.3f) aimWorld = ShoulderWorld + rs.normalized * 3f;
                attackHeld |= pad.rightTrigger.isPressed;
            }
        }
        // weapon switch / reload
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.digit1Key.wasPressedThisFrame) SetSlot(0);
            if (kb.digit2Key.wasPressedThisFrame) SetSlot(1);
            if (kb.qKey.wasPressedThisFrame) SetSlot(1 - slot);
            if (kb.rKey.wasPressedThisFrame) Reload();
            if (kb.tKey.wasPressedThisFrame && HasGun) CycleStance();
        }
        if (!scriptedAim.HasValue && mouse != null && Mathf.Abs(mouse.scroll.ReadValue().y) > 0.1f) SetSlot(1 - slot);
        if (pad != null && pad.buttonNorth.wasPressedThisFrame) SetSlot(1 - slot);
        if (reloadPending && Time.time >= reloadEnd) { Ammo = gunData.magazine; reloadPending = false; }
        // empty: the cell shows red for a beat, then the gun vents and reloads by itself
        if (HasGun && Ammo <= 0 && !reloadPending && Time.time - lastShot >= emptyReloadDelay) Reload();

        if (HasGun)
        {
            bool pressed = mouse != null && mouse.leftButton.wasPressedThisFrame || pad != null && pad.rightTrigger.wasPressedThisFrame;
            bool want = attackQueued || pressed || gunData.automatic && attackHeld;
            if (want && Time.time >= nextShot && !pc.IsSliding && !Reloading)
            {
                attackQueued = false;
                if (Ammo > 0) Fire(); else if (Time.time - lastShot >= emptyReloadDelay) Reload();
            }
            else if (!want || pc.IsSliding) attackQueued = false;
            return;
        }

        // a click during a step is kept (buffered) until the step can be cancelled into the next one
        if ((attackQueued || attackHeld) && Time.time >= nextAttack && !pc.IsSliding)
        {
            attackQueued = false;
            StartSwing();
        }
        else if (attackQueued && pc.IsSliding) attackQueued = false;
    }

    void StartSwing()
    {
        var steps = Steps;
        bool chain = comboIndex >= 0 && Time.time - lastEnd <= ComboWindow + (stepEnd - lastEnd);
        comboIndex = chain ? (comboIndex + 1) % steps.Length : 0;
        cur = steps[comboIndex];
        fromAngle = lastRel;
        swingStart = Time.time;
        float total = cur.windUp + cur.strike + cur.recover;
        stepEnd = lastEnd = swingStart + total;
        nextAttack = swingStart + cur.windUp + cur.strike + cur.recover * (weaponData != null ? weaponData.cancelAt : 0.5f);
        hitChecked = false; lungeDone = false;
        hitThisSwing.Clear();
        SwingCount++;
    }

    // keep an ankle target on its floor line but within 98% of the leg's length from the hip joint
    Vector2 Reachable(Transform thigh, Transform shin, Transform foot, Vector2 target)
    {
        if (thigh == null || shin == null || foot == null) return target;
        Vector2 j = M(thigh);
        float len = ((M(shin) - j).magnitude + (M(foot) - M(shin)).magnitude) * 0.98f;
        Vector2 v = target - j;
        if (v.magnitude <= len) return target;
        float dy = j.y - target.y;
        float dx = Mathf.Sqrt(Mathf.Max(0f, len * len - dy * dy));
        return new Vector2(j.x + Mathf.Sign(v.x) * dx, target.y);
    }

    // 2-bone leg IK in model space, knee bent forward (+X)
    void LegIK(Transform thigh, Transform shin, Transform foot, Vector2 target)
    {
        if (thigh == null || shin == null || foot == null) return;
        Vector2 j = M(thigh), k = M(shin), a = M(foot);
        float l1 = (k - j).magnitude, l2 = (a - k).magnitude;
        Vector2 v = target - j; float d = Mathf.Clamp(v.magnitude, Mathf.Abs(l1 - l2) + 1e-3f, l1 + l2 - 1e-3f);
        float alpha = Mathf.Acos(Mathf.Clamp((l1 * l1 + d * d - l2 * l2) / (2f * l1 * d), -1f, 1f)) * Mathf.Rad2Deg;
        Turn(thigh, Mathf.DeltaAngle(Ang(k - j), Ang(v) + alpha));
        k = M(shin); a = M(foot);
        Turn(shin, Mathf.DeltaAngle(Ang(a - k), Ang(target - k)));
    }

    float Guard(float sd) => sd > 0f ? restAngle : -restLow;
    static float EaseOut(float u) => 1f - Mathf.Pow(1f - Mathf.Clamp01(u), 3f);
    static float Smooth(float u) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u));
    Vector2 M(Transform t) => model.InverseTransformPoint(t.position);
    Vector2 M(Transform t, Vector3 local) => model.InverseTransformPoint(t.TransformPoint(local));
    float HandDir() => Ang(M(hand, Vector3.right * 0.1f) - M(hand));   // hand bone axis, mirror-safe (positions)
    static float Ang(Vector2 v) => Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;
    static Vector2 Dir(float deg) => new Vector2(Mathf.Cos(deg * Mathf.Deg2Rad), Mathf.Sin(deg * Mathf.Deg2Rad));
    static void Turn(Transform b, float deg) { if (b != null) b.localRotation = b.localRotation * Quaternion.Euler(0, 0, deg); }

    // Swing shape at time t since the click (current step). rel = blade offset from the aim line, body = +1 wind-up
    // -> -1 strike -> 0, ext = arm extension (0..1). Between steps the blade holds the last settle angle for the combo
    // window, then goes back to the high guard.
    void SwingShape(float t, out float rel, out float body, out float ext, out bool striking)
    {
        striking = false; ext = 0f; body = 0f;
        if (cur == null || t < 0f || Time.time >= stepEnd)
        {
            float since = Time.time - lastEnd;
            float settle = cur != null ? cur.settleAngle : GuardHigh;
            float k = Smooth((since - ComboWindow) / Mathf.Max(0.05f, weaponData != null ? weaponData.returnTime : 0.3f));
            rel = cur == null ? GuardHigh : Mathf.Lerp(settle, GuardHigh, k);
            return;
        }
        float wu = cur.windUp, st = cur.strike, rc = cur.recover;
        if (t < wu)
        {
            float u = EaseOut(t / wu);
            rel = Mathf.Lerp(fromAngle, cur.windAngle, u); body = u;
        }
        else if (t < wu + st)
        {
            float u = (t - wu) / st;
            rel = Mathf.Lerp(cur.windAngle, cur.throughAngle, EaseOut(u));
            body = Mathf.Lerp(1f, -1f, Smooth(u * 1.3f));
            ext = Mathf.Sin(Mathf.Clamp01(u) * Mathf.PI * 0.5f);
            striking = true;
        }
        else
        {
            float u = (t - wu - st) / rc;
            float k = Smooth((u - 0.25f) / 0.75f);   // hold the follow-through a moment, then settle
            rel = Mathf.Lerp(cur.throughAngle, cur.settleAngle, k); body = Mathf.Lerp(-1f, 0f, k); ext = 1f - k;
        }
    }

    // after the Animator: body turn, aim the near arm + sabre, turn the head a little toward the aim
    void LateUpdate()
    {
        bool show = !pc.IsSliding;
        weaponSr.enabled = show;
        if (!show && hands != null) { hands.Set(HandPoses.Side.Near, null); hands.Set(HandPoses.Side.Far, null); }
        if (!show || upper == null || fore == null || hand == null) return;

        Vector2 s0 = M(upper);
        Vector2 aimM = (Vector2)model.InverseTransformPoint(aimWorld) - s0;
        AimAngle = Mathf.Clamp(Ang(aimM), -100f, 100f);   // behind her only during a dash/slide facing lock

        float t = Time.time - swingStart;
        SwingShape(t, out float rel, out float body, out float ext, out bool striking);
        bool gun = HasGun;
        float recoil = 0f, reload = 0f;
        if (gun)
        {
            // recoil: snaps up on the shot and eases back; reload: the gun dips down and comes back up
            float rt = (Time.time - lastShot) / Mathf.Max(gunData.recoilTime, 1e-3f);
            recoil = rt < 1f ? Mathf.Pow(1f - rt, 2f) : 0f;
            if (Reloading)
            {
                float u = (Time.time - reloadStart) / Mathf.Max(gunData.reloadTime, 1e-3f);
                reload = Mathf.Sin(Mathf.Clamp01(u) * Mathf.PI);
            }
            rel = gunData.kickAngle * RecoilScale * recoil - 50f * reload; body = 0f; ext = 0f; striking = false;
        }
        else lastRel = rel;
        var stp = cur ?? Steps[0];
        // ---- whole-body posture ----
        float strikeW = Mathf.Max(0f, -body), windW = Mathf.Max(0f, body);
        guard = Mathf.MoveTowards(guard, IsSwinging ? 1f : 0f, Time.deltaTime * (IsSwinging ? 14f : 5f));
        Vector2 fr0 = M(footRear), ff0 = M(footFront);                       // planted feet from the animation
        Vector2 toeR0 = M(toeRear);                                          // (read before the pelvis lean moves the legs)
        float fra0 = Ang(M(toeRear) - fr0), ffa0 = Ang(M(toeFront) - ff0);  // and their angles
        // trunk: one straight line from the hips (+ = lean back): rise a little in the wind-up, lean in on the strike;
        // the neck takes it back so the head stays level and keeps looking at the target
        float lean = gun ? gunData.kickLean * recoil : -(windW * stp.windLean + strikeW * stp.strikeLean);
        // gun recoil stays in the upper body (chest), the hips and legs do not move; melee leans from the hips
        Turn(gun ? spine : hip, lean);
        Turn(neck, -lean * 0.7f);
        // free hand to the hip (elbow back), like the off hand of a one-handed sabre cut
        // free arm swings back with an open, spread hand for balance (sabre fencers' rear arm)
        Turn(farUpper, -50f * guard * freeHandOnHip); Turn(farFore, 30f * guard * freeHandOnHip);
        if (hands != null)
        {
            // free hand: spread while attacking or in the air, a fist when running, relaxed and open otherwise
            // (off by default: the open/spread drawings read as exaggerated - the rig's own fist is kept)
            string far = !freeHandPoses ? null : guard > 0.3f || !pc.IsGrounded ? "Spread" : Mathf.Abs(rb.linearVelocity.x) > 3.5f ? null : "Open";
            hands.Set(HandPoses.Side.Far, far);
        }

        // attack in place on the ground: long lunge step that lands with the strike (front knee bent, rear leg long,
        // hips low); legs are solved with 2-bone IK so the feet stay on the floor
        float want = pc.IsAttackStep ? ext : 0f;
        stance = Mathf.MoveTowards(stance, want, Time.deltaTime * 12f);
        // the feet are PLANTED in the world when the step starts: the lunge moves the body forward over them, the rear
        // toe stays where it was (no skating), the front foot lands `stride` ahead of where it started
        if (striking && !planted && pc.IsAttackStep && pc.IsGrounded)
        {
            planted = true;
            rearToeW = model.TransformPoint(toeR0);
            frontAnkleW = model.TransformPoint(ff0 + new Vector2(stp.stride, 0f));
        }
        if (stance <= 0.001f && !striking) planted = false;
        if (pc.IsGrounded && !pc.IsCrouching && (stance > 0.001f || !gun && Mathf.Abs(lean) > 0.01f))
        {
            float w = stance;
            if (hip != null) hip.position -= model.up * (stp.drop * w);
            Vector2 frontT = planted ? Vector2.Lerp(ff0, (Vector2)model.InverseTransformPoint(frontAnkleW), w) : ff0 + new Vector2(stp.stride * w, 0f);
            Vector2 toeT = planted ? Vector2.Lerp(toeR0, (Vector2)model.InverseTransformPoint(rearToeW), w) : toeR0 + new Vector2(-stp.back * w, 0f);
            frontT.y = ff0.y; toeT.y = toeR0.y + 0.028f * w;                  // on the floor line (the tilted toe cap would dip 0.03)
            // rear: the heel lifts 22 deg onto the ball of the foot with the TOE kept on the floor -> ankle from the toe
            float footAng = fra0 - 22f * w, footLen = (toeR0 - fr0).magnitude;
            Vector2 rearT = toeT - Dir(footAng) * footLen;
            // a planted foot the leg cannot reach any more slides along the floor (the rear foot drags in a real lunge)
            frontT = Reachable(legFront, shinFront, footFront, frontT);
            Vector2 rearR = Reachable(legRear, shinRear, footRear, rearT);
            if (rearR != rearT) { if (planted) rearToeW = model.TransformPoint(rearR + Dir(footAng) * footLen); rearT = rearR; }
            LegIK(legFront, shinFront, footFront, frontT);
            LegIK(legRear, shinRear, footRear, rearT);
            Turn(footFront, Mathf.DeltaAngle(Ang(M(toeFront) - M(footFront)), ffa0));   // front foot flat
            Turn(footRear, Mathf.DeltaAngle(Ang(M(toeRear) - M(footRear)), footAng));
        }

        float B = AimAngle + rel;    // wanted blade angle (model space)
        // Grip: the rig's drawn fist holds the blade ~90 deg off the forearm (guards, wind-ups). On the cut the hand
        // swaps to the in-line grip drawing (HandPoses "GripInline"), so the arm reaches out along the blade instead of
        // the wrist bending: the arm target blends from the perpendicular-grip range to the blade line.
        bool canInline = useInlineGrip && hands != null && hands.Find("GripInline") != null;
        bool drawnGrip = hands != null && hands.Find("Grip") != null;
        gripInline = Mathf.MoveTowards(gripInline, canInline && ext > 0.2f ? 1f : 0f, Time.deltaTime * 10f);
        float wMin = gun ? gunData.wristMin : bladeToForearmMin, wMax = gun ? gunData.wristMax : bladeToForearmMax;
        float perpArm = gun ? B - gunData.gripBladeOffset   // gun: hand in line with the forearm, barrel on the aim
                            : Mathf.Clamp(AimAngle + rel * (IsSwinging ? stp.armFollow : armFollow), B - bladeToForearmMax + 5f, B - bladeToForearmMin - 5f);
        float armAng = perpArm + Mathf.DeltaAngle(perpArm, B) * gripInline;

        // 2-bone IK: wrist on the line armAng from the shoulder. The elbow always bends to the same side (clockwise of
        // the shoulder->wrist line = below it when aiming forward), so it never flips over and spins the arm.
        Vector2 s = M(upper), e = M(fore), h = M(hand);
        float l1 = (e - s).magnitude, l2 = (h - e).magnitude;
        // arm length: pulled back in the wind-up (thrust), fully extended on the strike
        float r = gun ? (Stance == RangedWeapon.Stance.Weaver ? gunData.weaverReach : gunData.armReach) - gunData.kickBack * RecoilScale * recoil - 0.15f * reload
                      : Mathf.Lerp(Mathf.Lerp(armReach, stp.armReachWind, windW), l1 + l2 - 0.02f, ext);
        Vector2 target = s + Dir(armAng) * Mathf.Min(r, l1 + l2 - 0.01f);
        Vector2 v = target - s; float d = Mathf.Clamp(v.magnitude, Mathf.Abs(l1 - l2) + 1e-3f, l1 + l2 - 1e-3f);
        float alpha = Mathf.Acos(Mathf.Clamp((l1 * l1 + d * d - l2 * l2) / (2f * l1 * d), -1f, 1f)) * Mathf.Rad2Deg;
        Turn(upper, Mathf.DeltaAngle(Ang(e - s), Ang(v) - alpha));
        e = M(fore); h = M(hand);
        Turn(fore, Mathf.DeltaAngle(Ang(h - e), Ang(target - e)));
        e = M(fore); h = M(hand);

        // wrist: blade relative to the forearm. The hand bone is set ABSOLUTELY from the forearm each frame, so nothing
        // accumulates or spins. Which drawing is used follows that relation (hysteresis so it does not flicker).
        float F = Ang(h - e);
        float relW = Mathf.DeltaAngle(F, B);
        usingInline = canInline && relW < (usingInline ? 55f : 45f);
        float blade;
        if (usingInline)
        {
            blade = F + Mathf.Clamp(relW, -30f, 55f);                 // in-line grip: wrist within -30..55
            Turn(hand, Mathf.DeltaAngle(HandDir(), blade - bladeToHand + 90f));
            hands.Set(HandPoses.Side.Near, "GripInline", blade);       // drawn wrist->knuckles axis = the blade
            Vector2 wrist = M(hand);
            Vector2 grip = wrist + Dir(blade) * inlineGripDist;
            weapon.localPosition = new Vector3(grip.x, grip.y, -0.001f);
        }
        else
        {
            blade = F + Mathf.Clamp(relW, wMin, wMax);
            if (drawnGrip)
            {
                // NAI V5 grip drawing (back of the hand, thumb on top): the hilt crosses the fist at gripBladeOffset deg to
                // the wrist->knuckle axis, so that axis = blade - offset; the sabre is rigid at the drawn grip point
                float axis = blade - gripBladeOffset;
                Turn(hand, Mathf.DeltaAngle(HandDir(), axis - bladeToHand + 90f));
                hands.Set(HandPoses.Side.Near, "Grip", axis);
                Vector2 wrist = M(hand);
                Vector2 grip = wrist + (Vector2)(Quaternion.Euler(0, 0, axis) * gripLocal);
                weapon.localPosition = new Vector3(grip.x, grip.y, -0.001f);
            }
            else
            {
                Turn(hand, Mathf.DeltaAngle(HandDir(), blade - bladeToHand));
                if (hands != null) hands.Set(HandPoses.Side.Near, null);   // the rig's drawn fist
                Vector2 grip = M(hand, fistLocal);                         // sabre rigid in the fist, grip at the fist centre
                weapon.localPosition = new Vector3(grip.x, grip.y, -0.001f);
            }
        }
        weapon.localRotation = Quaternion.Euler(0, 0, blade);
        if (gun && TwoHanded) SupportArm(blade);
        if (gun && gauge != null) gauge.Show(Ammo, gunData.magazine, ReloadProgress, reloadFromEmpty, true);

        if (head != null) Turn(head, Mathf.Clamp(AimAngle * 0.25f, -10f, 14f));

        if (striking && !lungeDone) { lungeDone = true; pc.Lunge(AimDirWorld(), stp.lungeSpeed, airHang); SpawnSlash(); }
        if (striking && !hitChecked && t >= stp.windUp + stp.strike * stp.hitAt) { hitChecked = true; DoHits(); }
    }

    // TransformVector (not TransformDirection) so the model's mirrored scale flips the direction when facing left
    Vector2 AimDirWorld() => ((Vector2)model.TransformVector(Dir(AimAngle))).normalized;

    void DoHits()
    {
        var stp = cur ?? Steps[0];
        Vector2 origin = ShoulderWorld, dir = AimDirWorld();
        Vector2 knock = ((Vector2)model.TransformVector(Dir(AimAngle + stp.knockUp))).normalized;
        int n = Physics2D.OverlapCircle(origin, stp.reach, ContactFilter2D.noFilter, overlaps);
        bool any = false;
        for (int i = 0; i < n; ++i)
        {
            var c = overlaps[i];
            if (c == null || c.attachedRigidbody != null && c.attachedRigidbody.gameObject == gameObject) continue;
            var target = c.GetComponentInParent<IDamageable>();
            if (target == null || hitThisSwing.Contains(target)) continue;
            Vector2 p = c.ClosestPoint(origin);
            Vector2 to = p - origin;
            // inside the arc (angle to the collider's nearest point), or the blade line itself passes through it
            bool inArc = to.sqrMagnitude <= 1e-4f || Vector2.Angle(dir, to) <= stp.arcHalf;
            if (!inArc && !OnBladeLine(c, origin, dir, stp.reach, 0.15f)) continue;
            hitThisSwing.Add(target); HitCount++; any = true; LastHitDir = dir;
            target.TakeHit(stp.damage, knock * stp.knockback, p);
        }
        if (any && stp.hitStop > 0f) StartCoroutine(HitStop(stp.hitStop));
    }

    static bool OnBladeLine(Collider2D c, Vector2 origin, Vector2 dir, float reach, float width)
    {
        for (float k = 0.3f; k <= reach; k += 0.12f)
        {
            Vector2 q = origin + dir * k;
            if ((c.ClosestPoint(q) - q).sqrMagnitude <= width * width) return true;
        }
        return false;
    }

    IEnumerator HitStop(float seconds)
    {
        if (Time.timeScale < 1f) yield break;
        Time.timeScale = 0.05f;
        yield return new WaitForSecondsRealtime(seconds);
        Time.timeScale = 1f;
    }

    void SpawnSlash()
    {
        var stp = cur ?? Steps[0];
        if (slashSprite == null || stp.fx == MeleeWeapon.Fx.None) return;
        var go = new GameObject("Slash");
        go.transform.SetParent(transform, false);
        var sr = go.AddComponent<SpriteRenderer>(); sr.sortingOrder = 40; sr.color = slashColor;
        sr.sprite = stp.fx == MeleeWeapon.Fx.Streak ? StreakSprite() : slashSprite;
        // an upward sweep (through above wind) = counter-clockwise in model space
        StartCoroutine(SlashFade(go.transform, sr, stp.throughAngle > stp.windAngle, stp));
    }

    IEnumerator SlashFade(Transform t, SpriteRenderer sr, bool ccwInModel, MeleeWeapon.Step stp)
    {
        float life = stp.strike + stp.recover + 0.04f, t0 = Time.time, strike = stp.strike, reach = stp.reach;
        bool streak = stp.fx == MeleeWeapon.Fx.Streak;
        while (Time.time - t0 < life && t != null)
        {
            float k = (Time.time - t0) / life;
            Vector2 dir = AimDirWorld();
            bool ccwWorld = ccwInModel == (model.lossyScale.x > 0f);
            t.position = (Vector3)ShoulderWorld + Vector3.back * 0.01f;   // arc centred on the shoulder = the blade tip path
            t.rotation = Quaternion.Euler(0, 0, Ang(dir));
            float sc = Mathf.Lerp(0.92f, 1.06f, EaseOut(k * 2f)) * (reach / 1.25f) * 0.92f * stp.fxScale;
            // thrust: a thin streak along the aim from the shoulder to the blade tip
            if (streak) { t.position = (Vector3)(ShoulderWorld + dir * (reach * 0.5f)) + Vector3.back * 0.01f; t.localScale = new Vector3(reach * stp.fxScale * Mathf.Lerp(0.85f, 1.05f, EaseOut(k * 2f)), 1f, 1f); }
            else t.localScale = new Vector3(sc, ccwWorld ? sc : -sc, 1f);
            // sweeps in with the blade (first ~quarter), holds, then fades and thins
            var c = slashColor; float strikeK = strike / life;
            c.a *= k < strikeK ? Mathf.Lerp(0.4f, 1f, k / strikeK) : 1f - Smooth((k - strikeK) / (1f - strikeK));
            sr.color = c;
            yield return null;
        }
        if (t != null) Destroy(t.gameObject);
    }

    // thrust streak: 1 u long, 0.14 u thick, bright core, tail fading out toward the shoulder, pointed head
    static Sprite s_Streak;
    static Sprite StreakSprite()
    {
        if (s_Streak != null) return s_Streak;
        const int W = 256, H = 36;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        var px = new Color[W * H];
        for (int y = 0; y < H; ++y)
            for (int x = 0; x < W; ++x)
            {
                float u = (x + 0.5f) / W, v = Mathf.Abs((y + 0.5f) / H * 2f - 1f);
                float half = Mathf.Lerp(0.25f, 1f, Mathf.SmoothStep(0f, 1f, u / 0.85f)) * (u > 0.9f ? (1f - u) / 0.1f : 1f);
                float a = Mathf.Clamp01((half - v) / 0.25f) * Mathf.SmoothStep(0f, 1f, u * 1.3f);
                float core = Mathf.Clamp01(1f - v / Mathf.Max(half * 0.45f, 1e-3f));
                px[y * W + x] = new Color(Mathf.Lerp(0.55f, 1f, core), Mathf.Lerp(0.9f, 1f, core), 1f, a);
            }
        tex.SetPixels(px); tex.Apply();
        return s_Streak = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), W);
    }

    // Two-handed grip: the support (far) hand wraps under/behind the shooting fist. Weaver: its elbow bends down and
    // pulls back; Isosceles: the arm is as straight as the far shoulder's distance allows. The far arm is drawn behind
    // the body, so mostly the forearm and the fist under the gun show, as in a side photo of either stance.
    void SupportArm(float blade)
    {
        if (farUpper == null || farFore == null || farHand == null) return;
        Vector2 wrist = M(hand);
        Vector2 off = gunData.supportGrip;
        if (Stance == RangedWeapon.Stance.Isosceles) off += new Vector2(0.02f, 0.01f);
        Vector2 target = wrist + (Vector2)(Quaternion.Euler(0, 0, blade) * off);
        Vector2 s = M(farUpper), e = M(farFore), h = M(farHand);
        float l1 = (e - s).magnitude, l2 = (h - e).magnitude;
        Vector2 v = target - s; float d = Mathf.Clamp(v.magnitude, Mathf.Abs(l1 - l2) + 1e-3f, l1 + l2 - 1e-3f);
        float alpha = Mathf.Acos(Mathf.Clamp((l1 * l1 + d * d - l2 * l2) / (2f * l1 * d), -1f, 1f)) * Mathf.Rad2Deg;
        Turn(farUpper, Mathf.DeltaAngle(Ang(e - s), Ang(v) - alpha));     // elbow below the line (down)
        e = M(farFore); h = M(farHand);
        Turn(farFore, Mathf.DeltaAngle(Ang(h - e), Ang(target - e)));
        // support fist turned like the shooting fist (its drawn axis along the hand's axis)
        if (hands != null) Turn(farHand, Mathf.DeltaAngle(hands.RigAxis(HandPoses.Side.Far), blade - gripBladeOffset - 10f));
    }

    // ---- gun ----
    void Fire()
    {
        var g = gunData;
        Ammo--; ShotCount++;
        lastShot = Time.time; nextShot = Time.time + g.fireInterval;
        Vector2 muzzle = weapon.TransformPoint(new Vector3(g.muzzle.x, g.muzzle.y, 0f));
        // the barrel direction (follows the recoil of the previous shot a little, like a real follow-up)
        Vector2 barrel = ((Vector2)weapon.TransformVector(Vector3.right)).normalized;
        // from the MUZZLE to the cursor (the aim angle is measured from the shoulder: shooting along it would run
        // parallel to the cursor line, under or over it); a cursor right at the gun falls back to the barrel
        Vector2 toCursor = aimWorld - muzzle;
        Vector2 aim = toCursor.magnitude > 0.8f && Vector2.Dot(toCursor, barrel) > 0f ? toCursor.normalized : barrel;
        Vector2 dir = Vector2.Lerp(aim, barrel, 0.15f).normalized;
        float spread = g.spread * RecoilScale;
        dir = (Vector2)(Quaternion.Euler(0, 0, Random.Range(-spread, spread)) * dir);
        Projectile.Spawn(g.boltSprite, g.muzzleFlashSprite, muzzle, dir, g.boltSpeed, g.damage, g.knockback, g.range, gameObject);
        if (g.muzzleFlashSprite != null)
        {
            var f = new GameObject("MuzzleFlash");
            f.transform.SetParent(weapon, false);
            f.transform.localPosition = new Vector3(g.muzzle.x, g.muzzle.y, -0.01f);
            var sr = f.AddComponent<SpriteRenderer>(); sr.sprite = g.muzzleFlashSprite; sr.sortingOrder = 42;
            f.AddComponent<FlashFade>().Init(0.06f, 0.8f, 1.1f);
        }
        CameraFollow2D.Shake(g.shake, g.shakeTime);
        if (g.pushBack > 0f && pc.IsGrounded && rb != null) { rb.linearVelocity -= new Vector2(dir.x, 0f) * g.pushBack; pc.MarkStepInPlace(); }
    }

    void OnDisable() { if (Time.timeScale < 1f) Time.timeScale = 1f; }
}
