using System.Collections.Generic;
using UnityEngine;

// Dash effects for PlayerController2D: afterimages (frozen, tinted copies of the rig), speed lines, take-off dust.
// Afterimages copy the live bone pose onto pooled clones of the model, so they work with the deforming Sprite Skin
// rig and do not need any extra camera or render texture.
[RequireComponent(typeof(PlayerController2D))]
public class DashFX : MonoBehaviour
{
    [Header("Afterimages")]
    public float ghostInterval = 0.035f;
    public float ghostLife = 0.22f;
    public Color ghostColor = new Color(0.55f, 0.8f, 1f, 0.6f);
    public int poolSize = 8;

    [Header("Speed lines / dust")]
    public Color lineColor = new Color(1f, 1f, 1f, 0.7f);
    public Color dustColor = new Color(0.75f, 0.75f, 0.8f, 0.6f);

    class Ghost { public GameObject go; public Transform[] bones; public SpriteRenderer[] renderers; public float age = 99f; }

    PlayerController2D ctrl;
    Transform model;
    Transform[] liveBones;
    readonly List<Ghost> pool = new List<Ghost>();
    ParticleSystem lines, dust;
    bool wasDashing;
    float nextGhost;

    void Start()
    {
        ctrl = GetComponent<PlayerController2D>();
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
        dust = MakeDust(dustMat);
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
            var sh = lines.shape; sh.position = new Vector3(0.1f, 1.4f, 0);
            var vel = lines.velocityOverLifetime; vel.x = new ParticleSystem.MinMaxCurve(-Mathf.Sign(model.localScale.x) * 16f);
            if (!lines.isEmitting) lines.Play();
        }
        else if (lines.isEmitting) lines.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        wasDashing = dashing;

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
