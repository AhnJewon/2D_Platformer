using UnityEngine;

// Ammo gauge on the pistol's energy cell (the cyan bar along the slide). The gun sprite is copied into a runtime
// texture and only the cell's pixels are recoloured when the state changes:
//   - one segment per round, lit from the grip end; a fired round's segment goes dark
//   - empty: the whole cell turns red (pulsing) - the gun vents smoke and reloads
//   - reloading: the segments light up again one by one (red -> cyan) while the vents smoke
// The cell is found in the art itself (cyan pixels), so a repainted Pistol.xcf keeps working. Needs the texture
// readable (WeaponSpriteImport sets it for Pistol.png).
public class GunCellGauge : MonoBehaviour
{
    public Color emptyTint = new Color(0.12f, 0.16f, 0.2f, 1f);   // a spent segment: dark glass
    public Color redTint = new Color(1f, 0.18f, 0.12f, 1f);
    public int gapPx = 6;                                          // dark gap between segments (source px)

    SpriteRenderer sr;
    Sprite source, live;
    Texture2D tex;
    Color32[] orig;          // the cell rect's original pixels
    bool[] isCell;           // which of them belong to the cell
    RectInt rect;            // cell rect in texture px
    int x0, x1;              // lit range (columns)
    string lastKey;
    ParticleSystem smoke;

    public bool Ready => live != null;

    public void Init(SpriteRenderer target, Sprite gunSprite, Vector2[] ventsLocal)
    {
        sr = target; source = gunSprite;
        var src = gunSprite.texture;
        if (!src.isReadable) { Debug.LogWarning("GunCellGauge: " + src.name + " is not readable"); return; }
        var r = gunSprite.rect;
        int w = (int)r.width, h = (int)r.height;
        var px = src.GetPixels32();
        tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = src.filterMode, wrapMode = TextureWrapMode.Clamp };
        // copy the sprite's rect (single-sprite texture: the whole texture)
        var all = new Color32[w * h];
        for (int y = 0; y < h; ++y) System.Array.Copy(px, (y + (int)r.y) * src.width + (int)r.x, all, y * w, w);
        tex.SetPixels32(all); tex.Apply(false);

        // find the cell: strongly cyan pixels -> bounding rows/columns
        int minX = w, maxX = -1, minY = h, maxY = -1;
        bool StrongCyan(Color32 c) => c.a > 200 && c.g > 170 && c.b > 170 && c.r < 150;
        var rowCount = new int[h];
        for (int y = 0; y < h; ++y)
            for (int x = 0; x < w; ++x)
                if (StrongCyan(all[y * w + x])) { rowCount[y]++; if (x < minX) minX = x; if (x > maxX) maxX = x; }
        int best = 0; foreach (var c in rowCount) best = Mathf.Max(best, c);
        for (int y = 0; y < h; ++y) if (rowCount[y] > best * 0.3f) { minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y); }
        if (maxX < 0 || maxY < 0) { Debug.LogWarning("GunCellGauge: no energy cell found"); return; }
        rect = new RectInt(Mathf.Max(0, minX - 4), Mathf.Max(0, minY - 4), 0, 0);
        rect.width = Mathf.Min(w, maxX + 5) - rect.x; rect.height = Mathf.Min(h, maxY + 5) - rect.y;
        x0 = minX; x1 = maxX;
        orig = tex.GetPixels32();
        var block = new Color32[rect.width * rect.height]; isCell = new bool[block.Length];
        for (int y = 0; y < rect.height; ++y)
            for (int x = 0; x < rect.width; ++x)
            {
                var c = orig[(y + rect.y) * w + x + rect.x];
                block[y * rect.width + x] = c;
                isCell[y * rect.width + x] = c.a > 0 && c.g > c.r + 25 && c.b > c.r + 30;   // cell + its glow
            }
        orig = block;
        live = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(gunSprite.pivot.x / w, gunSprite.pivot.y / h), gunSprite.pixelsPerUnit, 0, SpriteMeshType.FullRect);
        live.name = gunSprite.name + "_gauge";

        // vent smoke (world space, rises and spreads)
        var go = new GameObject("VentSmoke"); go.transform.SetParent(target.transform, false);
        smoke = go.AddComponent<ParticleSystem>();
        smoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = smoke.main; main.loop = true; main.playOnAwake = false; main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.45f); main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.12f);
        main.startColor = new Color(0.85f, 0.88f, 0.92f, 0.55f); main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = -0.08f;
        var em = smoke.emission; em.rateOverTime = 45f;
        var sh = smoke.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 0.015f;
        sh.position = ventsLocal != null && ventsLocal.Length > 0 ? (Vector3)ventsLocal[0] : Vector3.zero;
        var sz = smoke.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.6f, 1, 2.2f));
        var col = smoke.colorOverLifetime; col.enabled = true;
        var gr = new Gradient(); gr.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(new Color(0.7f, 0.72f, 0.78f), 1) },
                                            new[] { new GradientAlphaKey(0.7f, 0), new GradientAlphaKey(0f, 1) });
        col.color = gr;
        var rend = go.GetComponent<ParticleSystemRenderer>();
        rend.material = new Material(Shader.Find("Sprites/Default")) { mainTexture = SoftDot(32) };
        rend.sortingOrder = target.sortingOrder + 1;
    }

    // ammo / magazine; reload: -1 = not reloading, else 0..1 progress; fromEmpty = this reload started with 0 rounds
    public void Show(int ammo, int magazine, float reload, bool fromEmpty, bool active)
    {
        if (!Ready) return;
        if (sr.sprite != live && active) sr.sprite = live;
        bool venting = active && reload >= 0f && fromEmpty && reload < 0.75f;
        if (venting && !smoke.isEmitting) smoke.Play(); else if (!venting && smoke.isEmitting) smoke.Stop(true, ParticleSystemStopBehavior.StopEmitting);

        int lit = reload >= 0f ? Mathf.FloorToInt(reload * magazine + 0.0001f) : ammo;
        bool red = reload < 0f && ammo <= 0;
        bool redRest = reload >= 0f && fromEmpty;   // refilling after empty: the not-yet-charged part stays red
        float pulse = red || redRest ? 0.7f + 0.3f * Mathf.Sin(Time.time * 18f) : 1f;
        string key = lit + "|" + magazine + "|" + red + "|" + redRest + "|" + Mathf.RoundToInt(pulse * 6);
        if (key == lastKey) return;
        lastKey = key;

        var block = new Color32[orig.Length];
        float seg = (x1 - x0 + 1f) / Mathf.Max(1, magazine);
        for (int y = 0; y < rect.height; ++y)
            for (int x = 0; x < rect.width; ++x)
            {
                int i = y * rect.width + x; var c = orig[i];
                if (!isCell[i]) { block[i] = c; continue; }
                int col = x + rect.x;
                float u = (col - x0) / seg;                       // segment index (float)
                int k = Mathf.Clamp(Mathf.FloorToInt(u), 0, magazine - 1);
                bool gap = (u - Mathf.Floor(u)) * seg < gapPx && col > x0 + 2 && col < x1 - 2;
                Color cc = c;
                float lum = Mathf.Max(cc.r, Mathf.Max(cc.g, cc.b));
                Color outc;
                if (red) outc = new Color(redTint.r * lum * pulse + 0.25f * lum, redTint.g * lum, redTint.b * lum, cc.a);
                else if (k < lit && !gap) outc = cc;                                       // charged round
                else if (redRest) outc = new Color(redTint.r * lum * pulse + 0.25f * lum, redTint.g * lum, redTint.b * lum, cc.a);
                else outc = new Color(Mathf.Lerp(emptyTint.r, cc.r, 0.12f), Mathf.Lerp(emptyTint.g, cc.g, 0.12f), Mathf.Lerp(emptyTint.b, cc.b, 0.12f), cc.a);
                if (gap && !red && !redRest) outc = new Color(emptyTint.r * 0.6f, emptyTint.g * 0.6f, emptyTint.b * 0.6f, cc.a);
                block[i] = outc;
            }
        tex.SetPixels32(rect.x, rect.y, rect.width, rect.height, block); tex.Apply(false);
    }

    void OnDestroy() { if (smoke != null) Destroy(smoke.gameObject); }

    public void Release(Sprite restore)
    {
        if (sr != null && sr.sprite == live) sr.sprite = restore;
        if (smoke != null && smoke.isEmitting) smoke.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }

    static Texture2D SoftDot(int n)
    {
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var p = new Color[n * n];
        for (int y = 0; y < n; ++y)
            for (int x = 0; x < n; ++x)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                p[y * n + x] = new Color(1, 1, 1, Mathf.Clamp01(1f - d));
            }
        t.SetPixels(p); t.Apply();
        return t;
    }
}
