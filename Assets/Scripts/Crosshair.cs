using UnityEngine;

// In-game crosshair at the aim point (PlayerCombat.AimWorld); the OS cursor is hidden while the game window has focus.
//   gun:   four ticks + centre dot; the ticks spread on each shot (recoil bloom) and settle back; while reloading a
//          ring fills around it; with an empty magazine it turns red
//   melee: a small ring
// Drawn as world sprites (sorting order 100) scaled by the camera's size, so it keeps the same screen size.
// Put it on the Player (next to PlayerCombat) or anywhere; it finds PlayerCombat itself.
[DefaultExecutionOrder(200)]   // after PlayerCombat updated the aim
public class Crosshair : MonoBehaviour
{
    public float size = 0.045f;          // tick length, as a fraction of the camera's half height
    public float gap = 0.03f;            // tick gap from the centre (same unit)
    public float bloom = 0.04f;          // extra gap right after a shot
    public float thickness = 0.009f;     // tick width (same unit)
    public float bloomTime = 0.18f;
    public Color color = new Color(1f, 1f, 1f, 0.95f);
    public Color outline = new Color(0.05f, 0.07f, 0.12f, 0.85f);
    public Color emptyColor = new Color(1f, 0.3f, 0.25f, 0.95f);
    public Color reloadColor = new Color(0.45f, 0.95f, 1f, 0.95f);
    public bool hideSystemCursor = true;

    PlayerCombat combat;
    Transform root;
    SpriteRenderer[] ticks = new SpriteRenderer[4], tickBg = new SpriteRenderer[4];
    SpriteRenderer dot, dotBg, ring, ringBg, reload;
    static Sprite s_Bar, s_Dot, s_Ring;
    Texture2D reloadTex; int lastReloadStep = -1;

    void Start()
    {
        combat = FindAnyObjectByType<PlayerCombat>();
        root = new GameObject("Crosshair").transform;
        for (int i = 0; i < 4; ++i) { tickBg[i] = Make("TickBg", Bar(), 100); ticks[i] = Make("Tick", Bar(), 101); }
        dotBg = Make("DotBg", Dot(), 100); dot = Make("Dot", Dot(), 101);
        ringBg = Make("RingBg", Ring(), 100); ring = Make("Ring", Ring(), 101);
        reloadTex = new Texture2D(64, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        reload = Make("Reload", Sprite.Create(reloadTex, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f), 64f), 101);
    }

    SpriteRenderer Make(string n, Sprite sp, int order)
    {
        var go = new GameObject(n); go.transform.SetParent(root, false);
        var r = go.AddComponent<SpriteRenderer>(); r.sprite = sp; r.sortingOrder = order;
        return r;
    }

    void OnApplicationFocus(bool f) { if (hideSystemCursor) Cursor.visible = !f; }
    void OnDisable() { Cursor.visible = true; if (root) root.gameObject.SetActive(false); }
    void OnEnable() { if (root) root.gameObject.SetActive(true); }

    void LateUpdate()
    {
        var cam = Camera.main;
        if (combat == null || cam == null) return;
        if (hideSystemCursor && Application.isFocused) Cursor.visible = false;
        float u = cam.orthographicSize;                       // world units per "half screen height"
        Vector2 p = combat.AimWorld;
        root.position = new Vector3(p.x, p.y, cam.transform.position.z + 1f);

        bool gun = combat.Crosshair == CrosshairStyle.Cross;
        float rp = combat.ReloadProgress;                     // -1 = not reloading
        bool empty = gun && combat.Ammo <= 0 && rp < 0f;
        Color c = empty ? emptyColor : color;

        // ticks (gun) / ring (melee)
        float k = gun ? Mathf.Clamp01(1f - combat.SinceShot / bloomTime) : 0f;
        float g = (gap + bloom * k) * u, L = size * u, w = thickness * u;
        for (int i = 0; i < 4; ++i)
        {
            ticks[i].enabled = tickBg[i].enabled = gun;
            if (!gun) continue;
            float a = i * 90f;
            Vector2 d = new Vector2(Mathf.Cos(a * Mathf.Deg2Rad), Mathf.Sin(a * Mathf.Deg2Rad));
            Place(ticks[i], d * (g + L * 0.5f), a, L, w, c);
            Place(tickBg[i], d * (g + L * 0.5f), a, L + 0.008f * u, w + 0.008f * u, outline);
        }
        dot.enabled = dotBg.enabled = gun;
        if (gun) { Place(dot, Vector2.zero, 0f, 0.011f * u, 0.011f * u, c); Place(dotBg, Vector2.zero, 0f, 0.019f * u, 0.019f * u, outline); }
        ring.enabled = ringBg.enabled = !gun;
        if (!gun) { float r = 0.06f * u; Place(ringBg, Vector2.zero, 0f, r * 1.1f, r * 1.1f, outline); Place(ring, Vector2.zero, 0f, r, r, color); }

        // reload ring (gun)
        reload.enabled = gun && rp >= 0f;
        if (reload.enabled)
        {
            int step = Mathf.RoundToInt(rp * 48f);
            if (step != lastReloadStep) { DrawArc(rp); lastReloadStep = step; }
            float r = (gap + size) * 2.3f * u; Place(reload, Vector2.zero, 0f, r, r, reloadColor);
        }
        else lastReloadStep = -1;
    }

    static void Place(SpriteRenderer r, Vector2 local, float deg, float lenX, float lenY, Color c)
    {
        var b = r.sprite.bounds.size;
        r.transform.localPosition = local;
        r.transform.localRotation = Quaternion.Euler(0, 0, deg);
        r.transform.localScale = new Vector3(lenX / b.x, lenY / b.y, 1f);
        r.color = c;
    }

    // clockwise arc from 12 o'clock, `t` of the full circle
    void DrawArc(float t)
    {
        var px = new Color32[64 * 64];
        for (int y = 0; y < 64; ++y)
            for (int x = 0; x < 64; ++x)
            {
                float dx = x + 0.5f - 32f, dy = y + 0.5f - 32f, d = Mathf.Sqrt(dx * dx + dy * dy);
                float ang = Mathf.Repeat(Mathf.Atan2(dx, dy) * Mathf.Rad2Deg, 360f) / 360f;
                float a = Mathf.Clamp01(1f - Mathf.Abs(d - 28.5f) / 2.2f);
                if (ang > t) a *= 0.22f;   // remaining part faint
                px[y * 64 + x] = new Color(1, 1, 1, a);
            }
        reloadTex.SetPixels32(px); reloadTex.Apply(false);
    }

    static Sprite Bar()
    {
        if (s_Bar) return s_Bar;
        var t = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        var px = new Color[16]; for (int i = 0; i < 16; ++i) px[i] = Color.white; t.SetPixels(px); t.Apply();
        return s_Bar = Sprite.Create(t, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
    }

    static Sprite Dot()
    {
        if (s_Dot) return s_Dot;
        const int n = 32; var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
        var px = new Color[n * n];
        for (int y = 0; y < n; ++y) for (int x = 0; x < n; ++x)
            { float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)); px[y * n + x] = new Color(1, 1, 1, Mathf.Clamp01(n / 2f - d)); }
        t.SetPixels(px); t.Apply();
        return s_Dot = Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
    }

    static Sprite Ring()
    {
        if (s_Ring) return s_Ring;
        const int n = 64; var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
        var px = new Color[n * n];
        for (int y = 0; y < n; ++y) for (int x = 0; x < n; ++x)
            { float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)); px[y * n + x] = new Color(1, 1, 1, Mathf.Clamp01(1f - Mathf.Abs(d - 26f) / 4.5f)); }
        t.SetPixels(px); t.Apply();
        return s_Ring = Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
    }
}
