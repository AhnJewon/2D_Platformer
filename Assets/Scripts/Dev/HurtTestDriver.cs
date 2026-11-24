using System.Collections;
using System.Text;
using UnityEngine;

// Dev tool: player hit reaction checks in the Playground (damage, knockback, stun, invincibility, hurt eyes) and frames
// in Screenshots/hurt_film/<name>_###.png (30 fps) for GIFs.
public class HurtTestDriver : MonoBehaviour
{
    public string report = "";
    public bool done;

    static void Snap(Vector2 at, string path, float size, int w, int h)
    {
        var cam = Camera.main;
        var oldPos = cam.transform.position; float oldSize = cam.orthographicSize;
        cam.transform.position = new Vector3(at.x, at.y, -10); cam.orthographicSize = size;
        var rt = new RenderTexture(w, h, 24); cam.targetTexture = rt; cam.Render();
        RenderTexture.active = rt; var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply(); RenderTexture.active = null; cam.targetTexture = null;
        cam.transform.position = oldPos; cam.orthographicSize = oldSize;
        Object.Destroy(rt);
        var full = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "Screenshots", path + ".png");
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full));
        System.IO.File.WriteAllBytes(full, tex.EncodeToPNG());
        Object.Destroy(tex);
    }

    IEnumerator Start()
    {
        var p = FindAnyObjectByType<PlayerController2D>();
        var hurt = p.GetComponent<PlayerHurt>();
        var c = p.GetComponent<PlayerCombat>();
        var rb = p.GetComponent<Rigidbody2D>();
        var sb = new StringBuilder();
        Vector2 Pos() => p.transform.position;
        IEnumerator For(float s) { float t = 0; while (t < s) { t += Time.deltaTime; yield return null; } }
        IEnumerator Film(string name, float sec, float x0)
        {
            int frames = Mathf.RoundToInt(sec * 30f);
            Time.captureFramerate = 30;
            for (int i = 0; i < frames; ++i)
            {
                if (i == 6) hurt.TakeHit(10f, new Vector2(-Mathf.Sign(p.model.lossyScale.x), 0f), Pos() + new Vector2(Mathf.Sign(p.model.lossyScale.x), 1.6f));
                yield return new WaitForEndOfFrame();
                Snap(new Vector2(x0 != 0f ? x0 : Pos().x, 1.5f), $"hurt_film/{name}_{i:000}", 2.0f, 560, 420);
                yield return null;
            }
            Time.captureFramerate = 0;
        }

        // 1. idle, facing right, hit from the front
        p.transform.position = new Vector3(-3f, 0f, 0); rb.linearVelocity = Vector2.zero;
        p.scriptedMove = 0f; c.scriptedAim = Pos() + new Vector2(3f, 1.6f);
        yield return For(0.6f);
        float h0 = hurt.health;
        hurt.TakeHit(10f, Vector2.left, Pos() + new Vector2(1f, 1.6f));
        yield return For(0.05f);
        sb.Append($"hit: health {h0}->{hurt.health} vx={rb.linearVelocity.x:F2} vy={rb.linearVelocity.y:F2} stunned={p.IsStunned} (expect 90, vx<0 pushed back)\n");
        var eyes = GameObject.Find("HurtEyes"); var rigEye = System.Array.Find(p.GetComponentsInChildren<SpriteRenderer>(true), r => r.name == "18_eye_main");
        sb.Append($"face: hurt eyes on={(eyes != null && eyes.GetComponent<SpriteRenderer>().enabled)} rig eye on={rigEye.enabled} (expect True, False)\n");
        // control is locked while stunned
        p.scriptedMove = 1f; yield return For(0.1f);
        sb.Append($"stunned input ignored: vx={rb.linearVelocity.x:F2} (expect <= 0)\n");
        // invincible: a second hit does nothing
        float h1 = hurt.health; hurt.TakeHit(10f, Vector2.left, Pos()); yield return null;
        sb.Append($"second hit while invincible: health {h1}->{hurt.health} (expect same)\n");
        p.scriptedMove = 0f; yield return For(1.2f);
        sb.Append($"after: invincible={hurt.Invincible} stunned={p.IsStunned} rig eye on={rigEye.enabled} (expect False, False, True)\n");
        hurt.TakeHit(10f, Vector2.left, Pos()); yield return null;
        sb.Append($"hit again after grace: health={hurt.health} (expect 80)\n");
        yield return For(1.3f);

        // 2. running right, hit from behind (pushed forward, leans forward)
        p.transform.position = new Vector3(-8f, 0f, 0); rb.linearVelocity = Vector2.zero; yield return null;
        p.scriptedMove = 1f; p.scriptedRun = true; yield return For(0.5f);
        hurt.TakeHit(10f, Vector2.right, Pos() + new Vector2(-1f, 1.2f)); yield return For(0.05f);
        sb.Append($"hit from behind while running: vx={rb.linearVelocity.x:F2} (expect > 0)\n");
        p.scriptedMove = 0f; yield return For(1.4f);

        // films
        p.transform.position = new Vector3(-3f, 0f, 0); rb.linearVelocity = Vector2.zero; p.scriptedMove = 0f; yield return For(0.5f);
        yield return Film("idle", 1.4f, -2.8f);
        yield return For(0.6f);
        c.SelectWeapon(c.GunData);
        p.transform.position = new Vector3(-8f, 0f, 0); rb.linearVelocity = Vector2.zero; p.scriptedMove = 1f; yield return For(0.4f);
        yield return Film("run", 1.2f, 0f);
        p.scriptedMove = null; c.scriptedAim = null;
        report = sb.ToString(); done = true;
    }
}
