using System.Collections.Generic;
using UnityEngine;

// Dash effects for PlayerController2D: afterimages (frozen, tinted copies of the rig), speed lines, take-off dust.
// Slide effects: a dust trail kicked up at the heels and the dragging hand/coat, flying dirt bits, speed lines.
// Afterimages copy the live bone pose onto pooled clones of the model, so they work with the deforming Sprite Skin
// rig and do not need any extra camera or render texture.
[RequireComponent(typeof(PlayerController2D))]
public class DashFX : MonoBehaviour
{
    [Header("Afterimages")]
    public float ghostInterval = 0.035f;
    public float ghostLife = 0.22f;
    public Color ghostColor = new Color(0.55f, 0.8f, 1f, 0.6f);
    public float slideGhostInterval = 0.045f;   // slide: copies of the hand-drawn slide sprite (the rig is hidden)
    public int poolSize = 8;

    [Header("Speed lines / dust")]
    public Color lineColor = new Color(1f, 1f, 1f, 0.7f);
    public Color dustColor = new Color(0.75f, 0.75f, 0.8f, 0.6f);

    [Header("Slide")]
    public Color slideDustColor = new Color(0.76f, 0.66f, 0.55f, 0.9f);   // earthy, so it reads against a pale sky too
    public Color dirtColor = new Color(0.42f, 0.38f, 0.36f, 1f);
    // contact points of the slide sprite, in units from the player origin along her facing (sprite at 407 px/unit,
    // pivot x 682 px): heels ~1100 px, dragging hand/coat ~350 px
    public float heelX = 1.0f, handX = -0.75f;

    class Ghost { public GameObject go; public Transform[] bones; public SpriteRenderer[] renderers; public float age = 99f; }
    class SpriteGhost { public SpriteRenderer sr; public float age = 99f; }
    readonly List<SpriteGhost> spritePool = new List<SpriteGhost>();
    SlideVisual slideVisual;
    float nextSlideGhost;

    PlayerController2D ctrl;
    Transform model;
    Transform[] liveBones;
    readonly List<Ghost> pool = new List<Ghost>();
    ParticleSystem lines, dust, heelDust, heelPuff, handDust, dirt;
    bool wasDashing, wasSliding;
    float nextGhost;

    void Start()
    {
        ctrl = GetComponent<PlayerController2D>();
        slideVisual = GetComponent<SlideVisual>();
        var anim = GetComponentInChildren<Animator>();
        model = anim ? anim.transform : transform;
        liveBones = model.GetComponentsInChildren<Transform>(true);

        // pooled frozen copies of the model
        for (int i = 0; i < poolSize; ++i)
        {
            var copy = Instantiate(model.gameObject);
            copy.name = "DashGhost_" + i;
            foreach (var a in copy.GetComponentsInChildren<Animator>(true)) a.enabled = false;
            foreach (var mb in copy.GetComponentsInChildren<MonoBehaviour>(true))
                if (!(mb is UnityEngine.U2D.Animation.SpriteSkin)) mb.enabled = false;
            var g = new Ghost { go = copy, bones = copy.GetComponentsInChildren<Transform>(true) };
            var rs = new List<SpriteRenderer>();
            foreach (var r in copy.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (r.name == "SlideSprite") { r.enabled = false; continue; }
                r.sortingOrder -= 100; rs.Add(r);
            }
            g.renderers = rs.ToArray();
            copy.SetActive(false);
            pool.Add(g);
        }

        var shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default") ?? Shader.Find("Sprites/Default");
        lines = MakeLines(new Material(shader));
        var dustMat = new Material(shader) { mainTexture = SoftDot(32) };   // round, soft puffs instead of squares
        var puffMat = new Material(shader) { mainTexture = Puff(32) };      // fuller core for the slide cloud
        dust = MakeDust(dustMat);
        // trailing cloud behind her (drawn behind the slide sprite so it does not haze over the legs),
        // plus short puffs in front right at the heels
        heelDust = MakeTrail("SlideHeelDust", puffMat, slideDustColor, 90f, new Vector2(0.28f, 0.55f), 25);
        heelPuff = MakeTrail("SlideHeelPuff", puffMat, slideDustColor, 60f, new Vector2(0.16f, 0.3f), 36);
        var pm = heelPuff.main; pm.startLifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.22f);
        handDust = MakeTrail("SlideHandDust", puffMat, slideDustColor, 45f, new Vector2(0.16f, 0.32f), 36);
        dirt = MakeDirt(new Material(shader) { mainTexture = SoftDot(8) });
    }

    void LateUpdate()
    {
        bool dashing = ctrl.IsDashing;
        if (dashing && !wasDashing)
        {
            nextGhost = Time.time + 0.045f;
            if (ctrl.IsGrounded) { dust.transform.position = transform.position + new Vector3(0, 0.15f, 0); dust.Play(); }
        }
        if (dashing)
        {
            if (Time.time >= nextGhost) { SpawnGhost(); nextGhost = Time.time + ghostInterval; }
            var sh = lines.shape; sh.position = new Vector3(0.1f, 1.4f, 0); sh.scale = new Vector3(1.2f, 2.8f, 0f);
            var vel = lines.velocityOverLifetime; vel.x = new ParticleSystem.MinMaxCurve(-Mathf.Sign(model.localScale.x) * 16f);
            if (!lines.isEmitting) lines.Play();
        }
        bool sliding = ctrl.IsSliding;
        if (sliding)
        {
            // afterimages like the dash, from the hand-drawn slide sprite
            if (!wasSliding) nextSlideGhost = Time.time + 0.03f;
            if (Time.time >= nextSlideGhost)
            {
                if (slideVisual != null && slideVisual.IsShowing) SpawnSpriteGhost(slideVisual.Renderer);   // not the rig: it is still upright for the first frame
                nextSlideGhost = Time.time + slideGhostInterval;
            }
            float f = Mathf.Sign(model.localScale.x);
            // floor is 0.08 above the player origin; puffs start a little above it so they never spread below the floor
            heelDust.transform.localPosition = new Vector3(f * heelX, 0.32f, 0);
            heelPuff.transform.localPosition = new Vector3(f * (heelX + 0.1f), 0.22f, 0);
            handDust.transform.localPosition = new Vector3(f * handX, 0.26f, 0);
            dirt.transform.localPosition = new Vector3(f * heelX, 0.14f, 0);
            // the heel dust is kicked forward and up; it trails behind on its own because it stays in world space
            SetVel(heelDust, f * 0.2f, f * 1.6f, 0.4f, 1.3f);
            SetVel(heelPuff, f * 1.0f, f * 2.5f, 0.3f, 0.9f);
            SetVel(handDust, -f * 0.3f, f * 0.4f, 0.2f, 0.7f);
            SetVel(dirt, f * 0.8f, f * 3.0f, 1.2f, 2.6f);
            if (!wasSliding) { heelDust.Emit(10); handDust.Emit(6); dirt.Emit(8); }
            foreach (var ps in new[] { heelDust, heelPuff, handDust, dirt }) if (!ps.isEmitting) ps.Play();
            var sh = lines.shape; sh.position = new Vector3(0.1f, 0.75f, 0); sh.scale = new Vector3(1.2f, 1.2f, 0f);   // low, around her
            var vel = lines.velocityOverLifetime; vel.x = new ParticleSystem.MinMaxCurve(-f * 16f);
            if (!lines.isEmitting) lines.Play();
        }
        else
        {
            foreach (var ps in new[] { heelDust, heelPuff, handDust, dirt })
                if (ps.isEmitting) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            if (!dashing && lines.isEmitting) lines.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
        wasDashing = dashing; wasSliding = sliding;

        foreach (var g in spritePool)
        {
            if (!g.sr.gameObject.activeSelf) continue;
            g.age += Time.deltaTime;
            float a = 1f - g.age / ghostLife;
            if (a <= 0f) { g.sr.gameObject.SetActive(false); continue; }
            var c = ghostColor; c.a *= a; g.sr.color = c;
        }
        foreach (var g in pool)
        {
            if (!g.go.activeSelf) continue;
            g.age += Time.deltaTime;
            float a = 1f - g.age / ghostLife;
            if (a <= 0f) { g.go.SetActive(false); continue; }
            var c = ghostColor; c.a *= a;
            foreach (var r in g.renderers) r.color = c;
        }
    }

    void SpawnSpriteGhost(SpriteRenderer src)
    {
        SpriteGhost g = null;
        foreach (var p in spritePool) if (!p.sr.gameObject.activeSelf) { g = p; break; }
        if (g == null)
        {
            if (spritePool.Count < 10)
            {
                var go = new GameObject("SlideGhost_" + spritePool.Count);
                g = new SpriteGhost { sr = go.AddComponent<SpriteRenderer>() };
                spritePool.Add(g);
            }
            else { g = spritePool[0]; foreach (var p in spritePool) if (p.age > g.age) g = p; }
        }
        var t = g.sr.transform;
        t.SetPositionAndRotation(src.transform.position + new Vector3(0, 0, 0.01f), src.transform.rotation);
        t.localScale = src.transform.lossyScale;
        g.sr.sprite = src.sprite; g.sr.flipX = src.flipX; g.sr.sortingOrder = src.sortingOrder - 1;
        g.sr.color = ghostColor; g.age = 0f;
        g.sr.gameObject.SetActive(true);
    }

    void SpawnGhost()
    {
        Ghost g = null;
        foreach (var p in pool) if (!p.go.activeSelf) { g = p; break; }
        if (g == null) { g = pool[0]; foreach (var p in pool) if (p.age > g.age) g = p; }
        g.go.transform.SetPositionAndRotation(model.position, model.rotation);
        g.go.transform.localScale = model.lossyScale;
        for (int i = 1; i < g.bones.Length && i < liveBones.Length; ++i)
        {
            g.bones[i].localPosition = liveBones[i].localPosition;
            g.bones[i].localRotation = liveBones[i].localRotation;
            g.bones[i].localScale = liveBones[i].localScale;
        }
        g.age = 0f;
        foreach (var r in g.renderers) r.color = ghostColor;
        g.go.SetActive(true);
    }

    static Texture2D SoftDot(int n)
    {
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color[n * n];
        for (int y = 0; y < n; ++y)
            for (int x = 0; x < n; ++x)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                px[y * n + x] = new Color(1, 1, 1, Mathf.Clamp01(1f - d) * Mathf.Clamp01(1f - d));
            }
        t.SetPixels(px); t.Apply();
        return t;
    }

    // soft disc with a solid middle (SoftDot fades from the centre, too faint for a dust cloud)
    static Texture2D Puff(int n)
    {
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color[n * n];
        for (int y = 0; y < n; ++y)
            for (int x = 0; x < n; ++x)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                px[y * n + x] = new Color(1, 1, 1, Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(0.45f, 1f, d)));
            }
        t.SetPixels(px); t.Apply();
        return t;
    }

    ParticleSystem MakeLines(Material mat)
    {
        var go = new GameObject("DashSpeedLines");
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main; main.loop = true; main.startLifetime = 0.14f; main.startSpeed = 0f; main.startSize = 0.035f;
        main.startColor = lineColor; main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = 200; main.playOnAwake = false;
        var em = ps.emission; em.rateOverTime = 80f;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(1.2f, 2.8f, 0f);
        var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(-16f); vel.y = new ParticleSystem.MinMaxCurve(0f); vel.z = new ParticleSystem.MinMaxCurve(0f);
        var col = ps.colorOverLifetime; col.enabled = true;
        var grad = new Gradient(); grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                                                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        col.color = grad;
        var r = ps.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.06f; r.lengthScale = 2f;
        r.material = mat; r.sortingOrder = -50;   // behind the character
        return ps;
    }

    static void SetVel(ParticleSystem ps, float x0, float x1, float y0, float y1)
    {
        var v = ps.velocityOverLifetime;
        v.x = new ParticleSystem.MinMaxCurve(Mathf.Min(x0, x1), Mathf.Max(x0, x1));
        v.y = new ParticleSystem.MinMaxCurve(y0, y1);
        v.z = new ParticleSystem.MinMaxCurve(0f, 0f);
    }

    // continuous puffs at a ground contact point; world space, so they stay where they were kicked up
    ParticleSystem MakeTrail(string name, Material mat, Color color, float rate, Vector2 size, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main; main.loop = true; main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.8f);
        main.startSpeed = 0f; main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
        main.startColor = color; main.gravityModifier = -0.02f; main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = 120;
        var em = ps.emission; em.rateOverTime = rate;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.SingleSidedEdge; sh.radius = 0.15f;   // horizontal line along the floor
        var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
        SetVel(ps, 0f, 0f, 0f, 0f);
        var lim = ps.limitVelocityOverLifetime; lim.enabled = true; lim.limit = 0.4f; lim.dampen = 0.08f;   // puffs slow down and hang
        var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.7f, 1, 1.8f));
        var col = ps.colorOverLifetime; col.enabled = true;
        var grad = new Gradient(); grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                                                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.9f, 0.5f), new GradientAlphaKey(0f, 1f) });
        col.color = grad;
        var r = ps.GetComponent<ParticleSystemRenderer>(); r.material = mat; r.sortingOrder = order;   // in front of the slide sprite (30)
        return ps;
    }

    // small dirt bits flicked forward and up from the heels, falling back with gravity
    ParticleSystem MakeDirt(Material mat)
    {
        var go = new GameObject("SlideDirt");
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main; main.loop = true; main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
        main.startSpeed = 0f; main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.07f);
        main.startColor = dirtColor; main.gravityModifier = 1.5f; main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = 60;
        var em = ps.emission; em.rateOverTime = 28f;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.SingleSidedEdge; sh.radius = 0.08f;
        var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
        SetVel(ps, 0f, 0f, 0f, 0f);
        var r = ps.GetComponent<ParticleSystemRenderer>(); r.material = mat; r.sortingOrder = 37;
        return ps;
    }

    ParticleSystem MakeDust(Material mat)
    {
        var go = new GameObject("DashDust");
        go.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);   // cone points up
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main; main.loop = false; main.duration = 0.1f; main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.4f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.8f); main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.32f);
        main.startColor = dustColor; main.gravityModifier = -0.05f; main.playOnAwake = false; main.simulationSpace = ParticleSystemSimulationSpace.World;
        var em = ps.emission; em.rateOverTime = 0f; em.SetBursts(new[] { new ParticleSystem.Burst(0f, 12) });
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 35f; sh.radius = 0.2f; sh.rotation = new Vector3(0, 0, 0);
        var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.6f, 1, 1.6f));
        var col = ps.colorOverLifetime; col.enabled = true;
        var grad = new Gradient(); grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                                                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        col.color = grad;
        var r = ps.GetComponent<ParticleSystemRenderer>(); r.material = mat; r.sortingOrder = 35;
        return ps;
    }
}
