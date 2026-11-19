using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

// Dev tool: scripted mouse-aim / sabre swings / backpedal checks in the Playground. Writes a report, snapshots in
// Screenshots/combat_*.png and frame sequences in Screenshots/combat_film/<name>_###.png (30 fps) for GIFs.
public class CombatTestDriver : MonoBehaviour
{
    public string report = "";
    public bool done;
    public bool film = true;

    static void Snap(Vector2 at, string path, float size = 2.2f, int w = 960, int h = 540)
    {
        var cam = Camera.main;
        var oldPos = cam.transform.position; float oldSize = cam.orthographicSize;
        cam.transform.position = new Vector3(at.x, at.y, -10); cam.orthographicSize = size;
        var rt = new RenderTexture(w, h, 24); cam.targetTexture = rt; cam.Render();
        RenderTexture.active = rt; var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply(); RenderTexture.active = null; cam.targetTexture = null;
        cam.transform.position = oldPos; cam.orthographicSize = oldSize;
        Object.Destroy(rt);
        var dir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "Screenshots");
        var full = System.IO.Path.Combine(dir, path + ".png");
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full));
        System.IO.File.WriteAllBytes(full, tex.EncodeToPNG());
        Object.Destroy(tex);
    }

    IEnumerator Start()
    {
        var p = FindAnyObjectByType<PlayerController2D>();
        var c = p.GetComponent<PlayerCombat>();
        var rb = p.GetComponent<Rigidbody2D>();
        var anim = p.GetComponentInChildren<Animator>();
        var dummy = FindObjectsByType<TrainingDummy>(FindObjectsSortMode.None);
        TrainingDummy near = null; foreach (var d in dummy) if (near == null || d.transform.position.x < near.transform.position.x) near = d;
        var sb = new StringBuilder();
        string State() { var s = anim.GetCurrentAnimatorStateInfo(0); foreach (var n in new[] { "Idle", "Walk", "Run", "Backpedal", "Crouch", "Air", "Land", "JumpStart", "Dash", "DashEnd", "SlideIn", "SlideOut" }) if (s.IsName(n)) return n; return "?"; }
        Vector2 Pos() => p.transform.position;
        IEnumerator For(float sec) { float t = 0; while (t < sec) { t += Time.deltaTime; yield return null; } }
        IEnumerator Shot(string name, float size = 2.2f) { yield return new WaitForEndOfFrame(); Snap(Pos() + new Vector2(0.4f, 1.4f), name, size); }
        // fixed 30 fps frames following the player
        IEnumerator Film(string name, float sec, System.Func<int, IEnumerator> each = null)
        {
            int frames = Mathf.RoundToInt(sec * 30f);
            Time.captureFramerate = 30;
            for (int i = 0; i < frames; ++i)
            {
                if (each != null) each(i);
                yield return new WaitForEndOfFrame();
                if (film) Snap(Pos() + new Vector2(0.4f, 1.4f), $"combat_film/{name}_{i:000}", 2.4f, 640, 400);
                yield return null;
            }
            Time.captureFramerate = 0;
        }

        // ---- 1. aim + swings at the dummy ----
        float dx = near.transform.position.x;
        p.transform.position = new Vector3(dx - 1.6f, 0f, 0); rb.linearVelocity = Vector2.zero;
        p.scriptedMove = 0f; p.scriptedRun = true;
        c.scriptedAim = new Vector2(dx, 1.6f);
        yield return For(0.5f);
        sb.Append($"aim at dummy: facing={p.Facing} aimAngle={c.AimAngle:F0}\n");
        yield return Shot("combat_aim_idle");
        int h0 = near.hits;
        var names = new List<string>();
        c.PressAttack(); yield return For(0.07f); names.Add(c.CurrentStep); yield return Shot("combat_swing_down"); yield return For(0.15f); names.Add("h" + (near.hits - h0) + " a" + c.AimAngle.ToString("F0"));
        yield return For(0.3f);
        c.PressAttack(); yield return For(0.1f); names.Add(c.CurrentStep); yield return Shot("combat_swing_up"); yield return For(0.15f); names.Add("h" + (near.hits - h0) + " x" + (near.transform.position.x - p.transform.position.x).ToString("F2"));
        yield return For(0.3f);
        c.PressAttack(); yield return For(0.1f); names.Add(c.CurrentStep); yield return For(0.45f); names.Add("h" + (near.hits - h0));
        sb.Append($"3 swings: swings={c.SwingCount} dummy hits={near.hits - h0} (expect 3) combo={string.Join(">", names)} (expect Cut>Thrust>RisingCut)\n");
        yield return For(0.9f);
        c.PressAttack(); yield return For(0.07f);
        sb.Append($"after a pause: step={c.CurrentStep} (expect Cut)\n");
        yield return For(0.5f);

        // ---- gun: 5 shots at the dummy from 4 u, then empty the magazine -> automatic reload ----
        if (c.gunData != null)
        {
            c.SetSlot(1);
            p.transform.position = new Vector3(dx - 4f, 0f, 0); rb.linearVelocity = Vector2.zero;
            c.scriptedAim = new Vector2(dx, 1.4f); yield return For(0.4f);
            h0 = near.hits; int a0 = c.Ammo, s0 = c.ShotCount;
            for (int k = 0; k < 5; ++k) { c.PressAttack(); yield return For(k == 0 ? 0.03f : 0.2f); if (k == 0) yield return Shot("gun_fire"); }
            yield return For(0.4f);
            sb.Append($"gun: shots={c.ShotCount - s0} dummy hits={near.hits - h0} (expect 5) ammo {a0}->{c.Ammo}\n");
            int guard = 0; while (c.Ammo > 0 && guard++ < 40) { c.PressAttack(); yield return For(0.18f); }
            yield return For(c.emptyReloadDelay + 0.1f); bool reloading = c.Reloading; yield return For(c.gunData.reloadTime + 0.2f);
            sb.Append($"gun: empty -> reloading={reloading}, after reload ammo={c.Ammo} (expect {c.gunData.magazine})\n");
            // facing left
            p.transform.position = new Vector3(dx + 4f, 0f, 0); rb.linearVelocity = Vector2.zero;
            c.scriptedAim = new Vector2(dx, 1.4f); yield return For(0.4f); h0 = near.hits;
            for (int k = 0; k < 3; ++k) { c.PressAttack(); yield return For(0.2f); }
            yield return For(0.4f);
            sb.Append($"gun facing left: facing={p.Facing} hits={near.hits - h0} (expect 3)\n");
            c.SetSlot(0); yield return For(0.2f);
        }

        // mirrored: dummy on her LEFT
        p.transform.position = new Vector3(dx + 1.6f, 0f, 0); rb.linearVelocity = Vector2.zero;
        c.scriptedAim = new Vector2(dx, 1.6f); yield return For(0.4f);
        h0 = near.hits;
        for (int k = 0; k < 3; ++k) { c.PressAttack(); yield return For(0.06f); if (k == 0) yield return Shot("combat_left_strike"); yield return For(0.34f); }
        sb.Append($"facing left: facing={p.Facing} dummy hits={near.hits - h0} (expect 3)\n");
        // dummy on the right while she faces left (cursor left): must NOT hit
        c.scriptedAim = Pos() + new Vector2(-3f, 1.6f); p.transform.position = new Vector3(dx - 1.4f, 0f, 0); yield return For(0.3f);
        h0 = near.hits; c.PressAttack(); yield return For(0.4f);
        sb.Append($"dummy behind her: hits={near.hits - h0} (expect 0)\n");
        // air swing: brief hang
        c.scriptedAim = new Vector2(dx, 2.2f); p.transform.position = new Vector3(dx - 1.6f, 0f, 0); yield return For(0.3f);
        p.PressJump(true); yield return For(0.45f); float vyBefore = rb.linearVelocity.y; c.PressAttack(); yield return For(0.13f);
        sb.Append($"air swing: vy before={vyBefore:F2} after strike={rb.linearVelocity.y:F2} hits so far={near.hits}\n");
        p.scriptedJumpHeld = false; yield return For(0.8f);

        // aim up / down / behind
        c.scriptedAim = Pos() + new Vector2(0.8f, 5f); yield return For(0.3f); yield return Shot("combat_aim_up");
        sb.Append($"aim up: facing={p.Facing} aimAngle={c.AimAngle:F0}\n");
        c.scriptedAim = Pos() + new Vector2(2f, -0.5f); yield return For(0.3f); yield return Shot("combat_aim_down");
        sb.Append($"aim low: aimAngle={c.AimAngle:F0}\n");
        c.scriptedAim = Pos() + new Vector2(-3f, 1.8f); yield return For(0.3f);
        sb.Append($"aim behind: facing={p.Facing} (expect -1)\n");
        // out of reach: no hit
        c.scriptedAim = new Vector2(dx, 1.6f); p.transform.position = new Vector3(dx - 3.5f, 0f, 0); yield return For(0.3f);
        h0 = near.hits; c.PressAttack(); yield return For(0.4f);
        sb.Append($"out of reach (3.5 u): hits={near.hits - h0} (expect 0)\n");

        // ---- 2. backpedal: aim right, move left ----
        p.transform.position = new Vector3(-3f, 0f, 0); rb.linearVelocity = Vector2.zero; yield return For(0.3f);
        p.scriptedMove = -1f;
        float tA = 0; while (tA < 0.5f) { c.scriptedAim = Pos() + new Vector2(4f, 1.2f); tA += Time.deltaTime; yield return null; }
        float x0 = Pos().x; tA = 0; while (tA < 0.5f) { c.scriptedAim = Pos() + new Vector2(4f, 1.2f); tA += Time.deltaTime; yield return null; }
        sb.Append($"backpedal: speed={(Pos().x - x0) / 0.5f:F2} u/s facing={p.Facing} state={State()}\n");
        yield return Shot("combat_backpedal");
        p.scriptedMove = 0f; yield return For(0.4f);
        sb.Append($"stop: state={State()}\n");

        // aim left, move right -> backpedal the other way
        p.scriptedMove = 1f;
        tA = 0; while (tA < 0.6f) { c.scriptedAim = Pos() + new Vector2(-4f, 1.2f); tA += Time.deltaTime; yield return null; }
        sb.Append($"backpedal (mirrored): vx={rb.linearVelocity.x:F2} facing={p.Facing} state={State()}\n");
        p.scriptedMove = 0f; yield return For(0.3f);

        // forward run while aiming ahead
        p.transform.position = new Vector3(-3f, 0f, 0); yield return null;
        p.scriptedMove = -1f;
        tA = 0; while (tA < 0.6f) { c.scriptedAim = Pos() + new Vector2(-4f, 1.2f); tA += Time.deltaTime; yield return null; }
        sb.Append($"run toward aim: vx={rb.linearVelocity.x:F2} state={State()}\n");
        p.scriptedMove = 0f; yield return For(0.3f);

        // ---- 3. crouch walk away from the aim plays reversed ----
        p.transform.position = new Vector3(-3f, 0f, 0); rb.linearVelocity = Vector2.zero; yield return For(0.2f);
        p.scriptedDown = true; yield return For(0.3f); p.scriptedMove = -1f;
        tA = 0; while (tA < 0.6f) { c.scriptedAim = Pos() + new Vector2(4f, 1.0f); tA += Time.deltaTime; yield return null; }
        sb.Append($"crouch backpedal: vx={rb.linearVelocity.x:F2} state={State()} MoveDir={anim.GetFloat("MoveDir")} speedMult={anim.GetCurrentAnimatorStateInfo(0).speedMultiplier:F1}\n");
        p.scriptedMove = 0f; p.scriptedDown = false; yield return For(0.4f);

        // ---- 4. dash goes the way you move, not the way you aim ----
        p.transform.position = new Vector3(-3f, 0f, 0); rb.linearVelocity = Vector2.zero; yield return For(0.2f);
        c.scriptedAim = Pos() + new Vector2(5f, 1.2f);
        p.scriptedMove = -1f; yield return For(0.15f); p.PressDash(); yield return For(0.08f);
        sb.Append($"dash while aiming right, moving left: vx={rb.linearVelocity.x:F1} (expect -11) facing during dash={p.Facing}\n");
        p.scriptedMove = 0f; yield return For(0.5f);
        sb.Append($"after dash facing={p.Facing} (back to the aim)\n");

        // ---- 5. films for GIFs ----
        if (film)
        {
            // run and slide (slide afterimages)
            p.transform.position = new Vector3(9f, 0f, 0); rb.linearVelocity = Vector2.zero; c.scriptedAim = Pos() + new Vector2(5f, 1.2f);
            p.scriptedMove = 1f; yield return For(0.5f);
            yield return Film("slide", 1.0f, i => { c.scriptedAim = Pos() + new Vector2(5f, 1.2f); if (i == 3) { p.scriptedDown = true; p.PressDash(); } return null; });
            p.scriptedDown = false; p.scriptedMove = 0f; yield return For(0.4f);
            // swings at the dummy, cursor sweeping
            p.transform.position = new Vector3(dx - 1.5f, 0f, 0); rb.linearVelocity = Vector2.zero; yield return For(0.3f);
            yield return Film("swings", 2.4f, i =>
            {
                float t = i / 30f;
                c.scriptedAim = new Vector2(dx + 0.3f, 1.6f + 1.4f * Mathf.Sin(t * 2.2f));
                if (i % 10 == 0) c.PressAttack();
                return null;
            });
            // same from the other side (facing left)
            p.transform.position = new Vector3(dx + 1.5f, 0f, 0); rb.linearVelocity = Vector2.zero; yield return For(0.3f);
            yield return Film("swings_left", 1.4f, i =>
            {
                c.scriptedAim = new Vector2(dx - 0.3f, 1.6f + 1.0f * Mathf.Sin(i / 30f * 2.2f));
                if (i % 10 == 0) c.PressAttack();
                return null;
            });
            // gun: shooting while the cursor sweeps over the dummy, then backpedalling while shooting
            if (c.gunData != null)
            {
                c.SetSlot(1);
                p.transform.position = new Vector3(dx - 3.5f, 0f, 0); rb.linearVelocity = Vector2.zero; yield return For(0.3f);
                c.SetAmmoForTest(5);
                yield return Film("gun", 2.6f, i => { c.scriptedAim = new Vector2(dx, 1.4f + 0.8f * Mathf.Sin(i / 9f)); if (i % 5 == 0 && i < 40) c.PressAttack(); return null; });
                c.SetSlot(0);
            }
            // backpedal away from the dummy while swinging
            p.transform.position = new Vector3(-2.4f, 0f, 0); rb.linearVelocity = Vector2.zero; yield return For(0.3f);
            p.scriptedMove = -1f;
            yield return Film("backpedal", 1.7f, i => { c.scriptedAim = Pos() + new Vector2(4f, 1.0f + 0.8f * Mathf.Sin(i / 12f)); if (i % 12 == 6) c.PressAttack(); return null; });
            p.scriptedMove = 0f;
        }

        c.scriptedAim = null; p.scriptedMove = null; p.scriptedDown = false;
        report = sb.ToString(); done = true;
    }
}
