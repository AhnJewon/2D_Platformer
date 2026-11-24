using UnityEngine;

// ---------------------------------------------------------------------------------------------------------------------
// Weapon modules. A weapon = a WeaponData asset (what it looks like, how it is held, its numbers) that creates a
// WeaponBehaviour (how it acts). PlayerCombat is the shared body: aim, facing, arm IK, holding the weapon in the
// hand drawing, whole-body posture (lean, lunge legs, free arm), slots and input. Adding a weapon:
//   - same kind (another sword, another gun): a new asset only (Create > Combat > Melee Weapon / Ranged Weapon)
//   - new kind (shotgun spread, charge bow, beam...): a WeaponData subclass + a WeaponBehaviour subclass
// Every frame (LateUpdate) PlayerCombat asks the behaviour for a WeaponPose, poses the body and arm from it, puts the
// weapon in the hand, then calls AfterPose with the final weapon angle (hits, shots, effects, two-hand support).
// ---------------------------------------------------------------------------------------------------------------------

public enum CrosshairStyle { Ring, Cross }

public abstract class WeaponData : ScriptableObject
{
    [Header("Art")]
    public Sprite sprite;                    // pivot = where the fist holds it
    public bool flipSprite;                  // mirror across its long axis (guard / trigger on the finger side)

    [Header("Grip (HandPoses drawing)")]
    public string gripPose = "Grip";
    public float gripBladeOffset = 77.4f;    // weapon angle - the drawing's wrist->knuckle axis
    public Vector2 gripLocal = new Vector2(0.158f, -0.019f);   // grip point from the wrist, in the hand frame (u)
    public float wristMin = 40f, wristMax = 125f;               // weapon angle relative to the forearm
    public float armReach = 0.62f;           // shoulder -> wrist at rest

    [Header("UI")]
    public CrosshairStyle crosshair = CrosshairStyle.Ring;

    public abstract WeaponBehaviour CreateBehaviour(PlayerCombat owner);
}

// What the behaviour wants the body to do this frame.
public struct WeaponPose
{
    public float rel;            // weapon angle relative to the aim line (deg, + = up)
    public float reach;          // shoulder -> wrist distance (clamped to the arm length)
    public bool armOnWeapon;     // true: the arm lines up with the weapon (guns); false: the arm leans toward it by
    public float armFollow;      //        armFollow and the wrist does the rest (swords)
    public float lean;           // trunk lean (deg, + = back)
    public bool leanFromChest;   // true: the chest takes it (hips and legs still); false: from the hips (whole trunk)
    public float ext;            // 0..1 strike extension: drives the lunge stance on an attack in place
    public bool striking;        // the feet plant when this turns on
    public float stride, back, drop;   // lunge stance: front foot forward, rear foot back, hips down (u)
    public float freeArm;        // 0..1 free arm swing back (balance)
}

public abstract class WeaponBehaviour
{
    protected readonly PlayerCombat owner;
    public readonly WeaponData data;
    protected WeaponBehaviour(PlayerCombat owner, WeaponData data) { this.owner = owner; this.data = data; }

    public virtual void OnEquip() { }
    public virtual void OnUnequip() { }
    public virtual void OnRemoved() { }       // dropped / swapped away: clean up what OnEquip created
    // Update: input. pressed = this frame, held = button down, queued = a buffered click (clear it when used)
    public abstract void Tick(bool pressed, bool held, ref bool queued);
    // LateUpdate, before the body is posed
    public abstract WeaponPose Pose();
    // LateUpdate, after the weapon is in the hand (weaponAngle = model space, deg)
    public virtual void AfterPose(float weaponAngle) { }
    public virtual void Reload() { }
    public virtual void AltAction() { }      // T: e.g. the gun's stance

    public virtual bool Busy => false;       // in the middle of an attack (free arm, lunge)
    public virtual string StateName => "";   // e.g. the combo step
    // HUD (crosshair, gauges); -1 / large = not applicable
    public virtual int Ammo => -1;
    public virtual int Magazine => -1;
    public virtual float ReloadProgress => -1f;
    public virtual float SinceShot => 99f;
}

public static class CombatMath
{
    public static float Ang(Vector2 v) => Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;
    public static Vector2 Dir(float deg) => new Vector2(Mathf.Cos(deg * Mathf.Deg2Rad), Mathf.Sin(deg * Mathf.Deg2Rad));
    public static float EaseOut(float u) => 1f - Mathf.Pow(1f - Mathf.Clamp01(u), 3f);
    public static float Smooth(float u) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u));
    public static void Turn(Transform b, float deg) { if (b != null) b.localRotation = b.localRotation * Quaternion.Euler(0, 0, deg); }
}
