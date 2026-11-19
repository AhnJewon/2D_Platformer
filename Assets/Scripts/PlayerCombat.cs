using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using static CombatMath;

// Anything a weapon can hit (training dummy, enemies).
public interface IDamageable { void TakeHit(float damage, Vector2 dir, Vector2 point); }

// The shared combat body. Weapons are modules (Assets/Scripts/Weapons, WeaponData.cs): each slot is a WeaponData
// asset whose WeaponBehaviour decides what the weapon does; this component does what every weapon needs:
//   - aim: mouse (or right stick) -> AimWorld / AimAngle; she faces the cursor (PlayerController2D asks DesiredFacing),
//     moving away from it is a backpedal
//   - input: click / hold -> the current behaviour; 1-9 pick a slot, Q / wheel / pad north cycle; R reload, T alt
//   - posture after the Animator, from the behaviour's WeaponPose: trunk lean (hips or chest), lunge stance with the
//     feet planted (2-bone leg IK), free arm swing, head turned a little toward the aim
//   - the near arm (2-bone IK, elbow always on the same side) and the weapon rigid in the drawn grip
//     (HandPoses gripPose), the wrist kept inside the weapon's range
//   - shared services for behaviours: hit-stop, two-hand support arm, aim helpers
[DefaultExecutionOrder(-50)]   // LateUpdate after the Animator but before SpriteSkin deformation (order 10)
[RequireComponent(typeof(PlayerController2D))]
public class PlayerCombat : MonoBehaviour
{
    [Header("Weapons")]
    public WeaponData[] weapons;             // slots (1, 2, 3...)
    public int slot;
    public int weaponSortingOrder = 26;      // under the near hand (27) so the grip sits in the fist

    [Header("Aim / body")]
    public float facingDeadZone = 0.12f;     // cursor this close to the body (x) keeps the current facing
    public bool freeHandPoses = false;       // swap the free hand to open / spread drawings (HandPoses); off = rig fist

    // scripted aim/attack (tests)
    public Vector2? scriptedAim;
    public void PressAttack() => attackQueued = true;

    // ---- state for behaviours, HUD and tests ----
    public WeaponBehaviour Current => behaviours != null && slot >= 0 && slot < behaviours.Length ? behaviours[slot] : null;
    public WeaponData CurrentData => Current?.data;
    public Vector2 AimWorld => aimWorld;
    public float AimAngle { get; private set; }      // model space, degrees (0 = straight ahead)
    public Transform Model => model;
    public Transform WeaponTransform => weapon;
    public SpriteRenderer WeaponRenderer => weaponSr;
    public PlayerController2D Controller => pc;
    public Rigidbody2D Body => rb;
    public float ArmLength { get; private set; }
    public Vector2 ShoulderWorld => upper != null ? (Vector2)upper.position : (Vector2)transform.position + Vector2.up * 2f;
    // TransformVector (not TransformDirection) so the model's mirrored scale flips the direction when facing left
    public Vector2 AimDirWorld() => ((Vector2)model.TransformVector(Dir(AimAngle))).normalized;
    public int SwingCount { get; set; }
    public int HitCount { get; set; }

    // convenience (HUD, tests)
    public CrosshairStyle Crosshair => CurrentData != null ? CurrentData.crosshair : CrosshairStyle.Ring;
    public bool HasGun => Current is GunBehaviour;
    public int Ammo => Current != null ? Current.Ammo : -1;
    public float ReloadProgress => Current != null ? Current.ReloadProgress : -1f;
    public float SinceShot => Current != null ? Current.SinceShot : 99f;
    public bool IsSwinging => Current != null && Current.Busy;
    public string CurrentStep => Current != null ? Current.StateName : "";
    GunBehaviour FirstGun { get { if (behaviours != null) foreach (var b in behaviours) if (b is GunBehaviour g) return g; return null; } }
    public RangedWeapon GunData => FirstGun?.data as RangedWeapon;
    public bool Reloading => FirstGun != null && FirstGun.Reloading;
    public int ShotCount => FirstGun != null ? FirstGun.ShotCount : 0;
    public void SetAmmoForTest(int n) => FirstGun?.SetAmmoForTest(n);
    public void ForceStep(int i) { if (Current is MeleeCombo m) { m.ForceStep(i); attackQueued = true; } }

    PlayerController2D pc;
    Rigidbody2D rb;
    HandPoses hands;
    Camera cam;
    WeaponBehaviour[] behaviours;
    Transform model, upper, fore, hand, farUpper, farFore, farHand, head, neck, spine, hip;
    Transform legRear, legFront, shinRear, shinFront, footRear, footFront, toeRear, toeFront;
    Transform weapon; SpriteRenderer weaponSr;
    float bladeToHand;       // weapon angle - hand bone angle for the rig's own fist (fallback without a grip drawing)
    Vector3 fistLocal;       // fist centre in the hand bone's space
    Vector2 aimWorld; bool attackQueued;
    float stance, freeArm;   // smoothed lunge stance / free arm swing
    bool planted; Vector3 rearToeW, frontAnkleW;   // lunge: feet fixed in the world during a strike

    void Awake()
    {
        pc = GetComponent<PlayerController2D>();
        rb = GetComponent<Rigidbody2D>();
        hands = GetComponent<HandPoses>();
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
        if (upper != null && fore != null && hand != null) ArmLength = (M(fore) - M(upper)).magnitude + (M(hand) - M(fore)).magnitude;
        // bind pose (before any animation): blade straight forward (0 deg) in the hanging fist
        if (hand != null)
        {
            bladeToHand = 0f - HandDir();
            foreach (var r in GetComponentsInChildren<SpriteRenderer>(true)) if (r.name == "13_hand_near") { fistLocal = hand.InverseTransformPoint(r.bounds.center); break; }
        }
        var w = new GameObject("Weapon"); weapon = w.transform; weapon.SetParent(model, false);
        weaponSr = w.AddComponent<SpriteRenderer>(); weaponSr.sortingOrder = weaponSortingOrder;
        aimWorld = (Vector2)transform.position + new Vector2(3f, 1.6f);
        int n = weapons != null ? weapons.Length : 0;
        behaviours = new WeaponBehaviour[n];
        for (int i = 0; i < n; ++i) behaviours[i] = weapons[i] != null ? weapons[i].CreateBehaviour(this) : null;
        int s0 = slot; slot = -1; SetSlot(s0);
    }

    public void SetSlot(int s)
    {
        if (behaviours == null || behaviours.Length == 0) return;
        s = Mathf.Clamp(s, 0, behaviours.Length - 1);
        if (behaviours[s] == null || s == slot) return;
        Current?.OnUnequip();
        slot = s;
        weaponSr.sprite = CurrentData.sprite; weaponSr.flipY = CurrentData.flipSprite;
        attackQueued = false;
        Current.OnEquip();
    }
    void Cycle(int d) { if (behaviours != null && behaviours.Length > 0) SetSlot((slot + d + behaviours.Length) % behaviours.Length); }

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
        var mouse = Mouse.current; var pad = Gamepad.current; var kb = Keyboard.current;
        bool held = false, pressed = false;
        if (scriptedAim.HasValue) aimWorld = scriptedAim.Value;
        else
        {
            if (mouse != null && cam != null)
            {
                Vector3 sp = mouse.position.ReadValue(); sp.z = -cam.transform.position.z;
                aimWorld = cam.ScreenToWorldPoint(sp);
                held |= mouse.leftButton.isPressed; pressed |= mouse.leftButton.wasPressedThisFrame;
            }
            if (pad != null)
            {
                var rs = pad.rightStick.ReadValue();
                if (rs.magnitude > 0.3f) aimWorld = ShoulderWorld + rs.normalized * 3f;
                held |= pad.rightTrigger.isPressed; pressed |= pad.rightTrigger.wasPressedThisFrame;
            }
            if (mouse != null && Mathf.Abs(mouse.scroll.ReadValue().y) > 0.1f) Cycle(mouse.scroll.ReadValue().y > 0f ? -1 : 1);
        }
        if (kb != null)
        {
            Key[] digits = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9 };
            for (int i = 0; i < digits.Length; ++i) if (kb[digits[i]].wasPressedThisFrame) SetSlot(i);
            if (kb.qKey.wasPressedThisFrame) Cycle(1);
            if (kb.rKey.wasPressedThisFrame) Current?.Reload();
            if (kb.tKey.wasPressedThisFrame) Current?.AltAction();
        }
        if (pad != null && pad.buttonNorth.wasPressedThisFrame) Cycle(1);
        Current?.Tick(pressed, held, ref attackQueued);
    }

    // after the Animator: posture, the near arm, the weapon in the hand
    void LateUpdate()
    {
        bool show = !pc.IsSliding && Current != null;
        weaponSr.enabled = show;
        if (!show && hands != null) { hands.Set(HandPoses.Side.Near, null); hands.Set(HandPoses.Side.Far, null); }
        if (!show || upper == null || fore == null || hand == null) return;
        var data = CurrentData;

        Vector2 aimM = (Vector2)model.InverseTransformPoint(aimWorld) - M(upper);
        AimAngle = Mathf.Clamp(Ang(aimM), -100f, 100f);   // behind her only during a dash/slide facing lock
        var pose = Current.Pose();

        // ---- trunk ----
        Vector2 fr0 = M(footRear), ff0 = M(footFront);                       // planted feet from the animation
        Vector2 toeR0 = M(toeRear);                                          // (read before the lean moves the legs)
        float fra0 = Ang(toeR0 - fr0), ffa0 = Ang(M(toeFront) - ff0);
        // + = lean back; the neck takes most of it back so the head stays level and keeps looking at the target
        Turn(pose.leanFromChest ? spine : hip, pose.lean);
        Turn(neck, -pose.lean * 0.7f);
        // free arm swings back for balance while attacking
        freeArm = Mathf.MoveTowards(freeArm, pose.freeArm, Time.deltaTime * (pose.freeArm > freeArm ? 14f : 5f));
        Turn(farUpper, -50f * freeArm); Turn(farFore, 30f * freeArm);
        if (hands != null)
            hands.Set(HandPoses.Side.Far, !freeHandPoses ? null : freeArm > 0.3f || !pc.IsGrounded ? "Spread" : Mathf.Abs(rb.linearVelocity.x) > 3.5f ? null : "Open");

        // ---- legs: attack in place on the ground = lunge (front knee bent, rear leg long, hips low), feet planted ----
        stance = Mathf.MoveTowards(stance, pc.IsAttackStep ? pose.ext : 0f, Time.deltaTime * 12f);
        if (pose.striking && !planted && pc.IsAttackStep && pc.IsGrounded)
        {
            planted = true;
            rearToeW = model.TransformPoint(toeR0);
            frontAnkleW = model.TransformPoint(ff0 + new Vector2(pose.stride, 0f));
        }
        if (stance <= 0.001f && !pose.striking) planted = false;
        if (pc.IsGrounded && !pc.IsCrouching && (stance > 0.001f || !pose.leanFromChest && Mathf.Abs(pose.lean) > 0.01f))
        {
            float w = stance;
            if (hip != null) hip.position -= model.up * (pose.drop * w);
            Vector2 frontT = planted ? Vector2.Lerp(ff0, (Vector2)model.InverseTransformPoint(frontAnkleW), w) : ff0 + new Vector2(pose.stride * w, 0f);
            Vector2 toeT = planted ? Vector2.Lerp(toeR0, (Vector2)model.InverseTransformPoint(rearToeW), w) : toeR0 + new Vector2(-pose.back * w, 0f);
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
            Turn(footFront, Mathf.DeltaAngle(Ang(M(toeFront) - M(footFront)), ffa0));
            Turn(footRear, Mathf.DeltaAngle(Ang(M(toeRear) - M(footRear)), footAng));
        }

        // ---- near arm: 2-bone IK; the elbow always bends below the shoulder->wrist line, so it never flips ----
        float B = AimAngle + pose.rel;    // wanted weapon angle (model space)
        float armAng = pose.armOnWeapon ? B - data.gripBladeOffset
                                        : Mathf.Clamp(AimAngle + pose.rel * pose.armFollow, B - data.wristMax + 5f, B - data.wristMin - 5f);
        Vector2 s = M(upper), e = M(fore), h = M(hand);
        float l1 = (e - s).magnitude, l2 = (h - e).magnitude;
        Vector2 target = s + Dir(armAng) * Mathf.Min(pose.reach, l1 + l2 - 0.01f);
        Vector2 v = target - s; float d = Mathf.Clamp(v.magnitude, Mathf.Abs(l1 - l2) + 1e-3f, l1 + l2 - 1e-3f);
        float alpha = Mathf.Acos(Mathf.Clamp((l1 * l1 + d * d - l2 * l2) / (2f * l1 * d), -1f, 1f)) * Mathf.Rad2Deg;
        Turn(upper, Mathf.DeltaAngle(Ang(e - s), Ang(v) - alpha));
        e = M(fore); h = M(hand);
        Turn(fore, Mathf.DeltaAngle(Ang(h - e), Ang(target - e)));
        e = M(fore); h = M(hand);

        // ---- weapon rigid in the fist; the wrist (weapon vs forearm) stays inside the weapon's range ----
        float F = Ang(h - e);
        float angle = F + Mathf.Clamp(Mathf.DeltaAngle(F, B), data.wristMin, data.wristMax);
        if (hands != null && hands.Find(data.gripPose) != null)
        {
            // grip drawing: the weapon crosses the fist at gripBladeOffset to the drawing's wrist->knuckle axis
            float axis = angle - data.gripBladeOffset;
            Turn(hand, Mathf.DeltaAngle(HandDir(), axis - bladeToHand + 90f));
            hands.Set(HandPoses.Side.Near, data.gripPose, axis);
            Vector2 grip = M(hand) + (Vector2)(Quaternion.Euler(0, 0, axis) * data.gripLocal);
            weapon.localPosition = new Vector3(grip.x, grip.y, -0.001f);
        }
        else
        {
            Turn(hand, Mathf.DeltaAngle(HandDir(), angle - bladeToHand));
            if (hands != null) hands.Set(HandPoses.Side.Near, null);   // the rig's own fist
            Vector2 grip = M(hand, fistLocal);
            weapon.localPosition = new Vector3(grip.x, grip.y, -0.001f);
        }
        weapon.localRotation = Quaternion.Euler(0, 0, angle);

        if (head != null) Turn(head, Mathf.Clamp(AimAngle * 0.25f, -10f, 14f));
        Current.AfterPose(angle);
    }

    // ---- shared services for weapon behaviours ----

    // brief freeze on a hit (real time)
    public void HitStop(float seconds) { if (seconds > 0f) StartCoroutine(HitStopCo(seconds)); }
    IEnumerator HitStopCo(float seconds)
    {
        if (Time.timeScale < 1f) yield break;
        Time.timeScale = 0.05f;
        yield return new WaitForSecondsRealtime(seconds);
        Time.timeScale = 1f;
    }

    // Two-handed hold: the support (far) hand wraps under/behind the weapon hand at `offset` (weapon frame, u from
    // the near wrist); its elbow bends down. The far arm is drawn behind the body, so mostly its forearm and fist show.
    public void SupportArm(float weaponAngle, Vector2 offset)
    {
        if (farUpper == null || farFore == null || farHand == null) return;
        Vector2 target = M(hand) + (Vector2)(Quaternion.Euler(0, 0, weaponAngle) * offset);
        Vector2 s = M(farUpper), e = M(farFore), h = M(farHand);
        float l1 = (e - s).magnitude, l2 = (h - e).magnitude;
        Vector2 v = target - s; float d = Mathf.Clamp(v.magnitude, Mathf.Abs(l1 - l2) + 1e-3f, l1 + l2 - 1e-3f);
        float alpha = Mathf.Acos(Mathf.Clamp((l1 * l1 + d * d - l2 * l2) / (2f * l1 * d), -1f, 1f)) * Mathf.Rad2Deg;
        Turn(farUpper, Mathf.DeltaAngle(Ang(e - s), Ang(v) - alpha));
        e = M(farFore); h = M(farHand);
        Turn(farFore, Mathf.DeltaAngle(Ang(h - e), Ang(target - e)));
        if (hands != null) Turn(farHand, Mathf.DeltaAngle(hands.RigAxis(HandPoses.Side.Far), weaponAngle - CurrentData.gripBladeOffset - 10f));
    }

    // ---- helpers ----
    Vector2 M(Transform t) => model.InverseTransformPoint(t.position);
    Vector2 M(Transform t, Vector3 local) => model.InverseTransformPoint(t.TransformPoint(local));
    float HandDir() => Ang(M(hand, Vector3.right * 0.1f) - M(hand));   // hand bone axis, mirror-safe (positions)

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

    void OnDisable() { if (Time.timeScale < 1f) Time.timeScale = 1f; }
}
