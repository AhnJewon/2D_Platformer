using System;
using UnityEngine;

// A melee weapon's look, grip and attack set (combo). PlayerCombat plays its steps in order: clicking again during
// a step, or within comboWindow after it, continues the combo; otherwise it starts over from step 0.
// Angles are the blade's offset from the aim line in degrees (+ = above the aim, toward the head).
// Sabre / longsword set (ArtSource/references/combat_backpedal_reference.md, "Combo"):
//   1 Cut        - overhead to forward-low diagonal cut with a step (Mega Man Zero 3 ground slash, kendo men)
//   2 Thrust     - arm pulled back, then fully extended straight at the aim with a long lunge (fencing lunge: the sword
//                  arm extends first, the front foot lands with the touch, rear leg straight)
//   3 Rising cut - from low behind the hip up to high, the body rising with it (longsword Unterhau: step in, cut up
//                  from the hip to the opposite shoulder, ends high); the finisher: more damage, knock-up, longer stop
[CreateAssetMenu(menuName = "Combat/Melee Weapon")]
public class MeleeWeapon : ScriptableObject
{
    public enum Kind { Cut, Thrust, RisingCut }
    public enum Fx { Arc, Streak, None }

    [Serializable] public class Step
    {
        public string name = "Cut";
        public Kind kind = Kind.Cut;
        [Header("Timing (s)")]
        public float windUp = 0.045f, strike = 0.075f, recover = 0.18f;
        [Header("Blade path (deg from the aim)")]
        public float windAngle = 95f;       // pulled back to here in the wind-up
        public float throughAngle = -40f;   // the strike sweeps to here
        public float settleAngle = -30f;    // and settles here (the next step starts from it)
        [Header("Arm")]
        public float armReachWind = 0.62f;  // shoulder -> wrist in the wind-up (thrust: pulled back)
        public float armFollow = 0.4f;      // how far the arm leans toward the blade (the wrist does the rest)
        [Header("Hit")]
        public float damage = 10f, reach = 2.1f, arcHalf = 80f, knockback = 5f;
        public float knockUp = 0f;          // knockback direction turned this far up from the aim
        public float hitStop = 0.05f;
        [Range(0, 1)] public float hitAt = 0.35f;   // fraction of the strike when the hit lands
        [Header("Body")]
        public float windLean = -4f;        // trunk lean from the hips in the wind-up (+ forward)
        public float strikeLean = 12f;      // ... on the strike
        public float lungeSpeed = 3.5f;     // ground step toward the aim
        public float stride = 0.5f, back = 0.35f, drop = 0.22f;   // lunge stance (attack in place on the ground)
        [Header("Effect")]
        public Fx fx = Fx.Arc;
        public float fxScale = 1f;
    }

    public Sprite sprite;
    public Sprite slashSprite;
    [Header("Grip (HandPoses drawing)")]
    public string gripPose = "Grip";
    public float gripBladeOffset = 77.4f;                    // hilt angle from the drawing's wrist->knuckle axis
    public Vector2 gripLocal = new Vector2(0.158f, -0.019f); // hilt centre from the wrist, in the hand frame (u)
    [Header("Guards and combo")]
    public float guardHigh = 70f;           // rest: blade this far above the aim
    public float comboWindow = 0.4f;        // after a step ends, a click within this continues the combo
    [Range(0, 1)] public float cancelAt = 0.5f;   // the next step may start after this much of the recovery
    public float returnTime = 0.3f;         // after the combo ends, back to the high guard in this time
    public Step[] combo = DefaultSabre();

    public static Step[] DefaultSabre() => new[]
    {
        new Step { name = "Cut", kind = Kind.Cut, windUp = 0.045f, strike = 0.075f, recover = 0.18f,
                   windAngle = 95f, throughAngle = -40f, settleAngle = -30f, armReachWind = 0.62f, armFollow = 0.4f,
                   damage = 10f, reach = 2.1f, arcHalf = 80f, knockback = 5f, hitStop = 0.05f,
                   windLean = -4f, strikeLean = 12f, lungeSpeed = 3.5f, stride = 0.5f, back = 0.35f, drop = 0.22f, fx = Fx.Arc },
        new Step { name = "Thrust", kind = Kind.Thrust, windUp = 0.07f, strike = 0.07f, recover = 0.2f,
                   windAngle = 0f, throughAngle = 0f, settleAngle = -10f, armReachWind = 0.3f, armFollow = 1f,
                   damage = 12f, reach = 2.6f, arcHalf = 18f, knockback = 7f, hitStop = 0.05f, hitAt = 0.6f,
                   windLean = -6f, strikeLean = 16f, lungeSpeed = 6f, stride = 0.75f, back = 0.4f, drop = 0.28f, fx = Fx.Streak, fxScale = 1.1f },
        new Step { name = "RisingCut", kind = Kind.RisingCut, windUp = 0.06f, strike = 0.08f, recover = 0.26f,
                   windAngle = -65f, throughAngle = 105f, settleAngle = 70f, armReachWind = 0.55f, armFollow = 0.5f,
                   damage = 15f, reach = 2.2f, arcHalf = 85f, knockback = 6f, knockUp = 55f, hitStop = 0.08f,
                   windLean = 10f, strikeLean = -8f, lungeSpeed = 3f, stride = 0.4f, back = 0.3f, drop = 0.25f, fx = Fx.Arc, fxScale = 1.1f },
    };
}
