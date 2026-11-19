using UnityEngine;

// A fast straight bolt. Each frame it raycasts the distance it is about to travel, so it cannot tunnel through thin
// targets. Hits an IDamageable (damage + knockback, impact flash) or stops on any solid collider; triggers that are
// not damageable and the shooter's own colliders are ignored. Dies after `range`.
public class Projectile : MonoBehaviour
{
    Vector2 dir; float speed, damage, knock, left;
    GameObject owner; Sprite impact;
    static readonly RaycastHit2D[] hits = new RaycastHit2D[8];

    public static Projectile Spawn(Sprite sprite, Sprite impactSprite, Vector2 pos, Vector2 dir, float speed, float damage,
                                   float knockback, float range, GameObject owner)
    {
        var go = new GameObject("Bolt");
        go.transform.position = new Vector3(pos.x, pos.y, -0.02f);
        go.transform.rotation = Quaternion.Euler(0, 0, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
        var sr = go.AddComponent<SpriteRenderer>(); sr.sprite = sprite; sr.sortingOrder = 41;
        var p = go.AddComponent<Projectile>();
        p.dir = dir.normalized; p.speed = speed; p.damage = damage; p.knock = knockback; p.left = range;
        p.owner = owner; p.impact = impactSprite;
        return p;
    }

    void Update()
    {
        float step = speed * Time.deltaTime;
        Vector2 pos = transform.position;
        int n = Physics2D.Raycast(pos, dir, ContactFilter2D.noFilter, hits, step);
        RaycastHit2D? best = null;
        for (int i = 0; i < n; ++i)
        {
            var h = hits[i];
            if (h.collider == null) continue;
            if (owner != null && (h.collider.attachedRigidbody != null && h.collider.attachedRigidbody.gameObject == owner || h.collider.transform.IsChildOf(owner.transform))) continue;
            bool damageable = h.collider.GetComponentInParent<IDamageable>() != null;
            if (h.collider.isTrigger && !damageable) continue;
            if (best == null || h.distance < best.Value.distance) best = h;
        }
        if (best.HasValue)
        {
            var h = best.Value;
            var target = h.collider.GetComponentInParent<IDamageable>();
            if (target != null) target.TakeHit(damage, dir * knock, h.point);
            Impact(h.point);
            Destroy(gameObject);
            return;
        }
        transform.position = pos + dir * step;
        left -= step;
        if (left <= 0f) Destroy(gameObject);
    }

    void Impact(Vector2 p)
    {
        if (impact == null) return;
        var go = new GameObject("BoltImpact");
        go.transform.position = new Vector3(p.x, p.y, -0.03f);
        go.transform.rotation = Quaternion.Euler(0, 0, Mathf.Atan2(-dir.y, -dir.x) * Mathf.Rad2Deg);   // bursts back toward the shooter
        var sr = go.AddComponent<SpriteRenderer>(); sr.sprite = impact; sr.sortingOrder = 46;
        go.AddComponent<FlashFade>().Init(0.09f, 0.45f, 0.75f);
    }
}

// Short pop-and-fade for flashes (muzzle flash, impacts). Unscaled time so it shows through hit-stop.
public class FlashFade : MonoBehaviour
{
    float life, s0, s1, t;
    SpriteRenderer sr;
    public void Init(float life, float startScale, float endScale) { this.life = life; s0 = startScale; s1 = endScale; sr = GetComponent<SpriteRenderer>(); Apply(); }
    void Apply()
    {
        float k = Mathf.Clamp01(t / life);
        float sc = Mathf.Lerp(s0, s1, 1f - (1f - k) * (1f - k));
        transform.localScale = new Vector3(sc, sc, 1f);
        if (sr) { var c = sr.color; c.a = 1f - k * k; sr.color = c; }
    }
    void Update() { t += Time.unscaledDeltaTime; Apply(); if (t >= life) Destroy(gameObject); }
}
