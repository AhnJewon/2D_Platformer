using System.Collections.Generic;
using UnityEngine;
using static CombatMath;

// The player getting hit (enemies, hazards, projectiles call TakeHit like on any IDamageable).
// References (ArtSource/References/hurt_reference.md):
//   - 2D platformers: knockback away from the hit with control taken away for a moment, then a grace period of
//     invincibility shown by a flicker (~1-1.5 s); a short red flash reads as "damage taken"
//   - hit reaction animation: the trunk snaps away from the impact first, the head follows a few frames later with a
//     larger overshoot (inertia chain), then everything eases back in; the feet stay put; a high hit drives the head
// The reaction is an ADDITIVE layer on whatever the Animator and PlayerCombat posed (runs after both, before the
// Sprite Skin), so it works while running, jumping or aiming. The face swaps to the hurt eyes (Face/Eye_Hurt) while it
// plays: the rig's eye parts are hidden and the drawing is shown on the head.
[DefaultExecutionOrder(-45)]   // after PlayerCombat (-50), before SpriteSkin deformation (10)
public class PlayerHurt : MonoBehaviour, IDamageable
{
    public float maxHealth = 100f;
    public float health;
    [Header("Reaction")]
    public float stunTime = 0.28f;           // no control
    public float invincibleTime = 1.1f;      // grace period (flicker)
    public Vector2 knockback = new Vector2(4.5f, 3.2f);   // away from the hit (x), up (y)
    public float reactTime = 0.38f;          // hit pose length
    public float trunkKick = 22f;            // deg the trunk snaps away from the hit
    public float headKick = 30f;             // the head goes further, a little later
    public float headDelay = 0.05f;
    public float hitStop = 0.06f;
    public float shake = 0.09f;
    [Header("Face")]
    public Sprite hurtEyes;                  // pivot = the eye's centre (ArtSource/Characters/Officer/Sprites/Eye_Hurt.xcf)
    public float faceTime = 0.5f;

    public bool Invincible => Time.time < invUntil;
    public int Hits { get; private set; }

    PlayerController2D pc;
    PlayerCombat combat;
    Transform model, spine, neck, head, hip, nearUpper, farUpper, nearFore, farFore;
    SpriteRenderer[] rig;
    readonly List<SpriteRenderer> eyes = new List<SpriteRenderer>();
    SpriteRenderer hurtEyeSr;
    float hitTime = -10f, hitReal = -10f, invUntil = -10f, side = 1f;
    public float flashTime = 0.1f;           // real seconds (not stretched by the hit-stop)
    public float flashStrength = 0.45f;

    void Awake()
    {
        pc = GetComponent<PlayerController2D>();
        combat = GetComponent<PlayerCombat>();
        var anim = GetComponentInChildren<Animator>();
        model = pc != null && pc.model != null ? pc.model : anim.transform;
        foreach (var t in GetComponentsInChildren<Transform>(true))
            switch (t.name)
            {
                case "spine": spine = t; break;
                case "neck": neck = t; break;
                case "head": head = t; break;
                case "hip": hip = t; break;
                case "arm_near_upper": nearUpper = t; break;
                case "arm_far_upper": farUpper = t; break;
                case "arm_near_fore": nearFore = t; break;
                case "arm_far_fore": farFore = t; break;
            }
        Vector3 eyeCentre = Vector3.zero; int order = 25;
        foreach (var r in model.GetComponentsInChildren<SpriteRenderer>(true))
            if (r.name == "18_eye_main")   // the eye itself; the brow line (19_eye_upper_detail) stays
            {
                eyes.Add(r);
                if (r.name == "18_eye_main") { eyeCentre = r.bounds.center; order = r.sortingOrder; }
            }
        if (head != null && hurtEyes != null)
        {
            // bind pose: put the hurt eyes where the drawn eye is, rigid on the head bone
            var go = new GameObject("HurtEyes");
            go.transform.SetParent(head, false);
            go.transform.position = eyeCentre;
            go.transform.rotation = model.rotation;
            go.transform.localScale = Vector3.one;
            hurtEyeSr = go.AddComponent<SpriteRenderer>(); hurtEyeSr.sprite = hurtEyes; hurtEyeSr.sortingOrder = order; hurtEyeSr.enabled = false;
        }
        health = maxHealth;
    }

    // every renderer of the character, including the weapon and hand drawings other components add in Awake
    void Start() { rig = model.GetComponentsInChildren<SpriteRenderer>(true); }

    public void TakeHit(float damage, Vector2 dir, Vector2 point)
    {
        if (Invincible || health <= 0f) return;
        health = Mathf.Max(0f, health - damage); Hits++;
        hitTime = Time.time; hitReal = Time.unscaledTime; invUntil = Time.time + invincibleTime;
        // pushed the way the hit travels (or away from where it came from)
        float sx = Mathf.Abs(dir.x) > 0.01f ? Mathf.Sign(dir.x) : Mathf.Sign(transform.position.x - point.x);
        if (sx == 0f) sx = -Mathf.Sign(model.lossyScale.x);
        // hit from the front (she faces it) = pushed back = lean back; from behind = lean forward
        side = Mathf.Sign(model.lossyScale.x) == -sx ? 1f : -1f;
        if (pc != null) pc.Knockback(new Vector2(sx * knockback.x, knockback.y), stunTime);
        if (combat != null) combat.HitStop(hitStop);
        CameraFollow2D.Shake(shake, 0.12f);
    }

    // debug: H = take a hit from the front
    void Update()
    {
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null && kb.hKey.wasPressedThisFrame)
            TakeHit(10f, new Vector2(-Mathf.Sign(model.lossyScale.x), 0f), (Vector2)transform.position + new Vector2(Mathf.Sign(model.lossyScale.x), 1.5f));
    }

    void LateUpdate()
    {
        float t = Time.time - hitTime;
        // ---- hit pose: snap (fast) -> hold a moment -> ease back; the head lags behind the trunk ----
        if (t < reactTime + headDelay)
        {
            float Env(float u) => u < 0f ? 0f : u < 0.18f ? EaseOut(u / 0.18f) : 1f - Smooth((u - 0.3f) / 0.7f);
            float k = Env(t / reactTime), kh = Env((t - headDelay) / reactTime);
            float back = side;                                  // + = lean back
            Turn(hip, back * trunkKick * 0.35f * k);
            Turn(spine, back * trunkKick * 0.65f * k);
            Turn(neck, back * headKick * 0.4f * kh);
            Turn(head, back * headKick * 0.6f * kh);
            // arms fly out a little (shoulders follow the chest, forearms lag)
            Turn(farUpper, back * 35f * kh); Turn(farFore, 25f * kh);
            if (combat == null || !combat.isActiveAndEnabled) { Turn(nearUpper, back * 25f * kh); Turn(nearFore, 20f * kh); }
        }
        // ---- face ----
        bool hurtFace = hurtEyeSr != null && t < faceTime;
        if (hurtEyeSr != null)
        {
            bool slide = SlideHidden();
            hurtEyeSr.enabled = hurtFace && !slide;
            if (!slide) foreach (var e in eyes) e.enabled = !hurtFace;
        }
        // ---- grace period: flicker; red flash right after the hit ----
        float red = Mathf.Clamp01(1f - (Time.unscaledTime - hitReal) / flashTime) * flashStrength;
        bool blinkOff = Invincible && t > 0.12f && Mathf.Repeat(Time.time, 0.12f) < 0.05f;
        var c = new Color(1f, 1f - red, 1f - red, blinkOff ? 0.35f : 1f);
        if (rig != null) foreach (var r in rig) if (r != null) r.color = c;
        if (hurtEyeSr != null) hurtEyeSr.color = c;
    }

    // the slide swaps the whole rig for a drawing (SlideVisual hides every rig part): leave the eyes hidden then
    bool SlideHidden() => pc != null && pc.IsSliding;
}
