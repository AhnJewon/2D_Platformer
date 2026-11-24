using System.Collections.Generic;
using UnityEngine;

// A weapon lying on the floor. It floats a little above the ground and bobs, with a soft light under it; when the
// player is close enough to take it (PlayerCombat.NearbyPickup) an "E" prompt appears. Taken by PlayerCombat.Interact:
// into a free slot, or swapped with the weapon in hand (that one is dropped here with a small toss).
// Place one in a scene with a WeaponData (Spawn also makes them at runtime).
public class WeaponPickup : MonoBehaviour
{
    public WeaponData data;
    public float hover = 0.35f;              // height of the weapon above the pickup's origin (= the floor)
    public float bob = 0.06f, bobSpeed = 2.4f;
    public float tilt = 20f;                 // lies tilted, muzzle / blade a little up

    static readonly List<WeaponPickup> all = new List<WeaponPickup>();
    SpriteRenderer art, glow;
    TextMesh prompt;
    float t0, landAt;
    Vector2 vel; bool flying;
    PlayerCombat player;

    public static WeaponPickup Nearest(Vector2 from, float range)
    {
        WeaponPickup best = null; float bd = range;
        foreach (var p in all)
        {
            if (p == null || p.flying || p.data == null) continue;
            float d = Vector2.Distance(from, (Vector2)p.transform.position + Vector2.up * p.hover);
            if (d <= bd) { bd = d; best = p; }
        }
        return best;
    }

    // a dropped weapon: tossed out sideways, falls to the floor below
    public static WeaponPickup Spawn(WeaponData d, Vector2 at, float side)
    {
        var go = new GameObject("Pickup_" + d.name);
        go.transform.position = at;
        var p = go.AddComponent<WeaponPickup>();
        p.data = d;
        p.vel = new Vector2(side * 2.2f, 3.2f); p.flying = true; p.landAt = Time.time + 0.25f;   // short block before it can be taken again
        return p;
    }

    void OnEnable() { all.Add(this); }
    void OnDisable() { all.Remove(this); }

    void Start()
    {
        t0 = Random.value * 10f;
        player = FindAnyObjectByType<PlayerCombat>();
        glow = Make("Glow", Glow(), -11); glow.color = new Color(1f, 0.95f, 0.75f, 0.45f);
        art = Make("Art", data != null ? data.sprite : null, -10);   // behind the character (rig parts use 0..29)
        if (data != null) art.flipY = data.flipSprite;
        var pg = new GameObject("Prompt"); pg.transform.SetParent(transform, false);
        prompt = pg.AddComponent<TextMesh>();
        prompt.text = "E"; prompt.anchor = TextAnchor.MiddleCenter; prompt.alignment = TextAlignment.Center;
        prompt.fontSize = 64; prompt.characterSize = 0.045f; prompt.fontStyle = FontStyle.Bold; prompt.color = Color.white;
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        prompt.font = font;
        var mr = pg.GetComponent<MeshRenderer>(); mr.sharedMaterial = font.material; mr.sortingOrder = 102;
        var bg = Make("PromptBg", Glow(), 101); bg.color = new Color(0.05f, 0.07f, 0.12f, 0.75f);
        bgR = bg;
    }
    SpriteRenderer bgR;

    SpriteRenderer Make(string n, Sprite sp, int order)
    {
        var go = new GameObject(n); go.transform.SetParent(transform, false);
        var r = go.AddComponent<SpriteRenderer>(); r.sprite = sp; r.sortingOrder = order;
        return r;
    }

    void Update()
    {
        if (flying)
        {
            vel.y -= 18f * Time.deltaTime;
            Vector2 p = transform.position, step = vel * Time.deltaTime;
            // land on the first solid surface below (ignores triggers and the player)
            var hit = Physics2D.Raycast(p + Vector2.up * 0.05f, Vector2.down, Mathf.Max(0f, -step.y) + 0.06f);
            if (vel.y < 0f && hit.collider != null && !hit.collider.isTrigger && hit.collider.GetComponentInParent<PlayerController2D>() == null)
            { transform.position = hit.point; flying = false; }
            else transform.position = p + step;
            if (transform.position.y < -20f) Destroy(gameObject);
        }
        float y = hover + (flying ? 0f : bob * Mathf.Sin((Time.time + t0) * bobSpeed));
        art.transform.localPosition = new Vector3(0f, y, 0f);
        art.transform.localRotation = Quaternion.Euler(0, 0, tilt);
        glow.transform.localPosition = new Vector3(0f, 0.06f, 0f);
        float len = art.sprite != null ? art.sprite.bounds.size.x : 0.6f;
        glow.transform.localScale = new Vector3(len * 1.3f, 0.12f, 1f) / Mathf.Max(glow.sprite.bounds.size.x, 1e-3f);
        glow.color = new Color(1f, 0.95f, 0.75f, 0.3f + 0.15f * Mathf.Sin((Time.time + t0) * bobSpeed));

        bool near = player != null && player.NearbyPickup == this && Time.time >= landAt;
        prompt.gameObject.SetActive(near); bgR.enabled = near;
        if (near)
        {
            prompt.transform.localPosition = new Vector3(0f, y + 0.42f, -0.01f);
            bgR.transform.localPosition = new Vector3(0f, y + 0.42f, 0f);
            bgR.transform.localScale = Vector3.one * (0.22f / Mathf.Max(bgR.sprite.bounds.size.x, 1e-3f));
        }
    }

    public void Take() { Destroy(gameObject); }

    static Sprite s_Glow;
    static Sprite Glow()
    {
        if (s_Glow) return s_Glow;
        const int n = 64; var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
        var px = new Color[n * n];
        for (int y = 0; y < n; ++y) for (int x = 0; x < n; ++x)
            { float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f); px[y * n + x] = new Color(1, 1, 1, Mathf.Clamp01(1f - d) * Mathf.Clamp01(1f - d)); }
        t.SetPixels(px); t.Apply();
        return s_Glow = Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
    }
}
