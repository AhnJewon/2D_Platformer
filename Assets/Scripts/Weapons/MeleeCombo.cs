using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static CombatMath;

// Melee behaviour: plays the MeleeWeapon's combo steps (wind-up -> strike -> recovery). A swing is a whole-body move
// (ArtSource/References/combat_backpedal_reference.md): the trunk leans from the hips, an attack in place steps into
// a lunge with the feet planted, the free arm swings back. Hits land part-way through the strike (arc around the
// aim from the shoulder, or along the blade line for thrusts); the slash trail shows during the strike.
public class MeleeCombo : WeaponBehaviour
{
    readonly MeleeWeapon w;
    MeleeWeapon.Step cur;
    int comboIndex = -1;
    float swingStart = -10f, stepEnd = -10f, lastEnd = -10f, nextAttack, fromAngle, lastRel;
    bool hitChecked, lungeDone, striking;
    float t;
    readonly Collider2D[] overlaps = new Collider2D[16];
    readonly HashSet<IDamageable> hitThisSwing = new HashSet<IDamageable>();

    public MeleeCombo(PlayerCombat owner, MeleeWeapon data) : base(owner, data) { w = data; lastRel = data.guardHigh; }

    MeleeWeapon.Step[] Steps => w.combo != null && w.combo.Length > 0 ? w.combo : (w.combo = MeleeWeapon.DefaultSabre());
    public override bool Busy => Time.time < stepEnd;
    public override string StateName => cur != null ? cur.name : "";
    public int ComboIndex => comboIndex;

    public override void OnUnequip() { stepEnd = Mathf.Min(stepEnd, Time.time); }

    // tests: make the next click play combo step i (0-based)
    public void ForceStep(int i) { comboIndex = i - 1; lastEnd = stepEnd = Time.time; nextAttack = 0f; }

    public override void Tick(bool pressed, bool held, ref bool queued)
    {
        // a click during a step is kept (buffered) until the step can be cancelled into the next one
        if ((queued || held) && Time.time >= nextAttack && !owner.Controller.IsSliding) { queued = false; StartSwing(); }
        else if (queued && owner.Controller.IsSliding) queued = false;
    }

    void StartSwing()
    {
        var steps = Steps;
        bool chain = comboIndex >= 0 && Time.time - lastEnd <= w.comboWindow + (stepEnd - lastEnd);
        comboIndex = chain ? (comboIndex + 1) % steps.Length : 0;
        cur = steps[comboIndex];
        fromAngle = lastRel;
        swingStart = Time.time;
        stepEnd = lastEnd = swingStart + cur.windUp + cur.strike + cur.recover;
        nextAttack = swingStart + cur.windUp + cur.strike + cur.recover * w.cancelAt;
        hitChecked = false; lungeDone = false;
        hitThisSwing.Clear();
        owner.SwingCount++;
    }

    // Swing shape at time t since the click. rel = blade offset from the aim line, body = +1 wind-up -> -1 strike -> 0,
    // ext = arm extension (0..1). Between steps the blade holds the last settle angle for the combo window, then goes
    // back to the high guard.
    void Shape(float t, out float rel, out float body, out float ext, out bool strikingNow)
    {
        strikingNow = false; ext = 0f; body = 0f;
        if (cur == null || t < 0f || Time.time >= stepEnd)
        {
            float since = Time.time - lastEnd;
            float k = Smooth((since - w.comboWindow) / Mathf.Max(0.05f, w.returnTime));
            rel = cur == null ? w.guardHigh : Mathf.Lerp(cur.settleAngle, w.guardHigh, k);
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
            strikingNow = true;
        }
        else
        {
            float u = (t - wu - st) / rc;
            float k = Smooth((u - 0.25f) / 0.75f);   // hold the follow-through a moment, then settle
            rel = Mathf.Lerp(cur.throughAngle, cur.settleAngle, k); body = Mathf.Lerp(-1f, 0f, k); ext = 1f - k;
        }
    }

    public override WeaponPose Pose()
    {
        t = Time.time - swingStart;
        Shape(t, out float rel, out float body, out float ext, out striking);
        lastRel = rel;
        var stp = cur ?? Steps[0];
        float strikeW = Mathf.Max(0f, -body), windW = Mathf.Max(0f, body);
        return new WeaponPose
        {
            rel = rel,
            reach = Mathf.Lerp(Mathf.Lerp(w.armReach, stp.armReachWind, windW), owner.ArmLength - 0.02f, ext),
            armOnWeapon = false,
            armFollow = Busy ? stp.armFollow : w.restArmFollow,
            // trunk: one straight line from the hips: rise a little in the wind-up, lean in on the strike
            lean = -(windW * stp.windLean + strikeW * stp.strikeLean),
            leanFromChest = false,
            ext = ext, striking = striking,
            stride = stp.stride, back = stp.back, drop = stp.drop,
            freeArm = Busy ? w.freeArmSwing : 0f,
        };
    }

    public override void AfterPose(float weaponAngle)
    {
        var stp = cur ?? Steps[0];
        if (striking && !lungeDone) { lungeDone = true; owner.Controller.Lunge(owner.AimDirWorld(), stp.lungeSpeed, w.airHang); SpawnSlash(); }
        if (striking && !hitChecked && t >= stp.windUp + stp.strike * stp.hitAt) { hitChecked = true; DoHits(); }
    }

    void DoHits()
    {
        var stp = cur ?? Steps[0];
        Vector2 origin = owner.ShoulderWorld, dir = owner.AimDirWorld();
        Vector2 knock = ((Vector2)owner.Model.TransformVector(Dir(owner.AimAngle + stp.knockUp))).normalized;
        int n = Physics2D.OverlapCircle(origin, stp.reach, ContactFilter2D.noFilter, overlaps);
        bool any = false;
        for (int i = 0; i < n; ++i)
        {
            var c = overlaps[i];
            if (c == null || c.attachedRigidbody != null && c.attachedRigidbody.gameObject == owner.gameObject) continue;
            var target = c.GetComponentInParent<IDamageable>();
            if (target == null || hitThisSwing.Contains(target)) continue;
            Vector2 p = c.ClosestPoint(origin);
            Vector2 to = p - origin;
            // inside the arc (angle to the collider's nearest point), or the blade line itself passes through it
            bool inArc = to.sqrMagnitude <= 1e-4f || Vector2.Angle(dir, to) <= stp.arcHalf;
            if (!inArc && !OnBladeLine(c, origin, dir, stp.reach, 0.15f)) continue;
            hitThisSwing.Add(target); owner.HitCount++; any = true;
            target.TakeHit(stp.damage, knock * stp.knockback, p);
        }
        if (any && stp.hitStop > 0f) owner.HitStop(stp.hitStop);
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

    // ---- slash trail ----
    void SpawnSlash()
    {
        var stp = cur ?? Steps[0];
        if (w.slashSprite == null || stp.fx == MeleeWeapon.Fx.None) return;
        var go = new GameObject("Slash");
        go.transform.SetParent(owner.transform, false);
        var sr = go.AddComponent<SpriteRenderer>(); sr.sortingOrder = 40; sr.color = w.slashColor;
        sr.sprite = stp.fx == MeleeWeapon.Fx.Streak ? StreakSprite() : w.slashSprite;
        // an upward sweep (through above wind) = counter-clockwise in model space
        owner.StartCoroutine(SlashFade(go.transform, sr, stp.throughAngle > stp.windAngle, stp));
    }

    IEnumerator SlashFade(Transform tr, SpriteRenderer sr, bool ccwInModel, MeleeWeapon.Step stp)
    {
        float life = stp.strike + stp.recover + 0.04f, t0 = Time.time, strike = stp.strike, reach = stp.reach;
        bool streak = stp.fx == MeleeWeapon.Fx.Streak;
        while (Time.time - t0 < life && tr != null)
        {
            float k = (Time.time - t0) / life;
            Vector2 dir = owner.AimDirWorld();
            bool ccwWorld = ccwInModel == (owner.Model.lossyScale.x > 0f);
            tr.position = (Vector3)owner.ShoulderWorld + Vector3.back * 0.01f;   // arc centred on the shoulder = the blade tip path
            tr.rotation = Quaternion.Euler(0, 0, Ang(dir));
            float sc = Mathf.Lerp(0.92f, 1.06f, EaseOut(k * 2f)) * (reach / 1.25f) * 0.92f * stp.fxScale;
            // thrust: a thin streak along the aim from the shoulder to the blade tip
            if (streak) { tr.position = (Vector3)(owner.ShoulderWorld + dir * (reach * 0.5f)) + Vector3.back * 0.01f; tr.localScale = new Vector3(reach * stp.fxScale * Mathf.Lerp(0.85f, 1.05f, EaseOut(k * 2f)), 1f, 1f); }
            else tr.localScale = new Vector3(sc, ccwWorld ? sc : -sc, 1f);
            // sweeps in with the blade (first ~quarter), holds, then fades and thins
            var c = w.slashColor; float strikeK = strike / life;
            c.a *= k < strikeK ? Mathf.Lerp(0.4f, 1f, k / strikeK) : 1f - Smooth((k - strikeK) / (1f - strikeK));
            sr.color = c;
            yield return null;
        }
        if (tr != null) Object.Destroy(tr.gameObject);
    }

    // thrust streak: bright core, tail fading out toward the shoulder, pointed head
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
}
