using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

// Dev tool: scripted dash (ground + air) and slide under the Low_Ceiling in the Playground, with a report.
public class DashSlideTestDriver : MonoBehaviour
{
    public string report = "";

    // renders the main camera, framed on the player, to Screenshots/<name>.png
    static void Snap(float x, string name)
    {
        var cam = Camera.main; var follow = cam.GetComponent<CameraFollow2D>();
        var oldPos = cam.transform.position; float oldSize = cam.orthographicSize;
        cam.transform.position = new Vector3(x + 0.3f, 1.3f, -10); cam.orthographicSize = 2.2f;
        var rt = new RenderTexture(960, 540, 24); cam.targetTexture = rt; cam.Render();
        RenderTexture.active = rt; var tex = new Texture2D(960, 540, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); tex.Apply(); RenderTexture.active = null; cam.targetTexture = null;
        cam.transform.position = oldPos; cam.orthographicSize = oldSize;
        var dir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "Screenshots");
        System.IO.Directory.CreateDirectory(dir);
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
    }
    public bool done;

    IEnumerator Start()
    {
        var p = FindAnyObjectByType<PlayerController2D>();
        var rb = p.GetComponent<Rigidbody2D>();
        var anim = p.GetComponentInChildren<Animator>();
        var sb = new StringBuilder();
        string State() { var s = anim.GetCurrentAnimatorStateInfo(0); foreach (var n in new[] { "Idle", "Walk", "Run", "JumpStart", "Air", "Land", "Dash", "DashEnd", "SlideIn", "SlideOut" }) if (s.IsName(n)) return n; return "?"; }
        var seen = new List<string>();
        void Track() { var s = State(); if (seen.Count == 0 || seen[seen.Count - 1] != s) seen.Add(s); }
        IEnumerator For(float sec) { float t = 0; while (t < sec) { Track(); t += Time.deltaTime; yield return null; } }

        yield return For(0.5f);
        // ground dash to the right from a standstill
        p.scriptedMove = 0f; seen.Clear();
        float x0 = p.transform.position.x; p.PressDash();
        float td = 0; bool snapped = false;
        while (td < 0.6f) { Track(); if (!snapped && p.IsDashing && td > 0.12f) { yield return new WaitForEndOfFrame(); Snap(p.transform.position.x - 0.6f, "dash_fx"); snapped = true; } td += Time.deltaTime; yield return null; }
        sb.Append($"ground dash: moved {p.transform.position.x - x0:F2} units, states={string.Join(">", seen)}\n");

        // air dash: jump then dash at the top
        seen.Clear(); p.PressJump(true); yield return For(0.35f);
        float y1 = p.transform.position.y; x0 = p.transform.position.x; p.PressDash(); yield return For(0.25f);
        sb.Append($"air dash: dx={p.transform.position.x - x0:F2} dy={p.transform.position.y - y1:F2} (flat), ");
        p.scriptedJumpHeld = false; yield return For(1.0f);
        sb.Append($"landed grounded={p.IsGrounded} states={string.Join(">", seen)}\n");

        // teleport in front of the low ceiling and slide under it
        p.transform.position = new Vector3(12.5f, -0.08f, 0); rb.linearVelocity = Vector2.zero; yield return For(0.3f);
        p.scriptedMove = 1f; p.scriptedRun = true; yield return For(0.25f);
        seen.Clear(); p.scriptedDown = true; p.PressDash(); yield return For(0.1f); p.scriptedDown = false;
        bool spriteSeen = false; float t2 = 0; float minTop = 99f;
        int shots = 0;
        while (t2 < 1.6f)
        {
            Track();
            var ss = GameObject.Find("SlideSprite");
            bool on = ss && ss.GetComponent<SpriteRenderer>().enabled;
            spriteSeen |= on;
            minTop = Mathf.Min(minTop, p.GetComponent<Collider2D>().bounds.max.y);
            // snapshots: just before the swap (rig), during the slide (sprite), just after (rig)
            string st = State();
            if ((shots == 0 && st == "SlideIn") || (shots == 1 && on) || (shots == 2 && st == "SlideOut")) { yield return new WaitForEndOfFrame(); Snap(p.transform.position.x, "slide_" + shots++); }
            t2 += Time.deltaTime; yield return null;
        }
        sb.Append($"slide: x={p.transform.position.x:F2} (ceiling spans 14.75..17.25), lowest collider top={minTop:F2}, slide sprite shown={spriteSeen}, states={string.Join(">", seen)}\n");
        p.scriptedMove = null;
        report = sb.ToString(); done = true;
    }
}
