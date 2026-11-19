using UnityEngine;

// Target for testing attacks: flashes, wobbles away from the hit, shows a hit spark and counts the damage.
// Health refills after a short pause, so it never breaks.
public class TrainingDummy : MonoBehaviour, IDamageable
{
    public float maxHealth = 100f;
    public float health;
    public int hits;
    public float lastDamage;
    public Sprite sparkSprite;   // optional; the slash sprite works

    SpriteRenderer sr;
    Material flashMat;
    float wobble, wobbleVel, lastHitTime = -10f;
    Vector3 basePos;

    void Awake()
    {
        sr = GetComponentInChildren<SpriteRenderer>();
        health = maxHealth;
        basePos = sr.transform.localPosition;
    }

    public void TakeHit(float damage, Vector2 dir, Vector2 point)
    {
        health -= damage; hits++; lastDamage = damage; lastHitTime = Time.time;
        wobbleVel += Mathf.Clamp(dir.x, -6f, 6f) * -60f;   // tip away from the hit
        SpawnSpark(point, dir);
        StartCoroutine(Flash(point, dir));
    }

    void SpawnSpark(Vector2 p, Vector2 dir)
    {
        var go = new GameObject("HitSpark");
        go.transform.position = new Vector3(p.x, p.y, -0.02f);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main; main.duration = 0.2f; main.loop = false; main.startLifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.25f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 7f); main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.05f);
        main.startColor = new Color(1f, 0.95f, 0.75f); main.useUnscaledTime = true; main.stopAction = ParticleSystemStopAction.Destroy;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var em = ps.emission; em.rateOverTime = 0; em.SetBursts(new[] { new ParticleSystem.Burst(0f, 10) });
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 35f; sh.radius = 0.02f;
        go.transform.rotation = Quaternion.LookRotation(new Vector3(dir.x, dir.y, 0f).normalized + new Vector3(0, 0, 0.0001f), Vector3.back);
        var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1, 1, 0));
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.material = new Material(Shader.Find("Sprites/Default")); r.sortingOrder = 45;
        r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.03f; r.lengthScale = 4f;
        ps.Play();
    }

    // impact flash: a 4-point star that pops and fades (unscaled time, so it shows through the hit-stop)
    static Sprite s_Star;
    static Sprite Star()
    {
        if (s_Star != null) return s_Star;
        const int N = 128; var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
        var px = new Color32[N * N];
        for (int y = 0; y < N; ++y)
            for (int x = 0; x < N; ++x)
            {
                float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                float ax = Mathf.Abs(dx), ay = Mathf.Abs(dy);
                // thin rays along the axes + soft core
                float ray = Mathf.Max(Mathf.Clamp01(1f - ay / (0.09f * (1f - ax) + 0.005f)) * (1f - ax),
                                      Mathf.Clamp01(1f - ax / (0.09f * (1f - ay) + 0.005f)) * (1f - ay));
                float core = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy) / 0.35f);
                float a = Mathf.Clamp01(ray + core * core);
                px[y * N + x] = new Color(1f, 1f, 0.92f, a);
            }
        tex.SetPixels32(px); tex.Apply();
        return s_Star = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 128f);
    }

    System.Collections.IEnumerator Flash(Vector2 p, Vector2 dir)
    {
        var go = new GameObject("HitFlash");
        go.transform.position = new Vector3(p.x, p.y, -0.03f);
        go.transform.rotation = Quaternion.Euler(0, 0, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg + 15f);
        var r = go.AddComponent<SpriteRenderer>(); r.sprite = Star(); r.sortingOrder = 46;
        float t = 0f, life = 0.14f;
        while (t < life)
        {
            float k = t / life;
            float sc = Mathf.Lerp(0.5f, 1.3f, 1f - (1f - k) * (1f - k));
            go.transform.localScale = new Vector3(sc * 1.4f, sc * 0.8f, 1f);
            r.color = new Color(1f, 1f, 0.9f, 1f - k * k);
            t += Time.unscaledDeltaTime; yield return null;
        }
        Destroy(go);
    }

    void Update()
    {
        // spring wobble around the base
        wobbleVel += (-wobble * 260f - wobbleVel * 9f) * Time.deltaTime;
        wobble += wobbleVel * Time.deltaTime;
        sr.transform.localRotation = Quaternion.Euler(0, 0, Mathf.Clamp(wobble, -25f, 25f));
        float since = Time.time - lastHitTime;
        sr.color = since < 0.08f ? new Color(1f, 0.8f, 0.78f) : Color.white;
        sr.transform.localPosition = basePos + (since < 0.1f ? (Vector3)(Random.insideUnitCircle * 0.03f) : Vector3.zero);
        if (since > 2f) health = maxHealth;
    }
}
