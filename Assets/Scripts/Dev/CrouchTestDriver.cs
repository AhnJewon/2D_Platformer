using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

// Dev tool: scripted crouch / crouch walk / stay crouched under a ceiling / slide -> crouch / crouch -> slide
// in the Playground, with a report and snapshots in Screenshots/crouch_*.png.
public class CrouchTestDriver : MonoBehaviour
{
    public string report = "";
    public bool done;

    static void Snap(float x, string name)
    {
        var cam = Camera.main;
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

    IEnumerator Start()
    {
        var p = FindAnyObjectByType<PlayerController2D>();
        var rb = p.GetComponent<Rigidbody2D>();
        var col = p.GetComponent<CapsuleCollider2D>();
        var anim = p.GetComponentInChildren<Animator>();
        var sb = new StringBuilder();
        string State() { var s = anim.GetCurrentAnimatorStateInfo(0); foreach (var n in new[] { "Idle", "Walk", "Run", "JumpStart", "Air", "Land", "Dash", "DashEnd", "SlideIn", "SlideOut", "Crouch" }) if (s.IsName(n)) return n; return "?"; }
        var seen = new List<string>();
        void Track() { var s = State(); if (seen.Count == 0 || seen[seen.Count - 1] != s) seen.Add(s); }
        IEnumerator For(float sec) { float t = 0; while (t < sec) { Track(); t += Time.deltaTime; yield return null; } }
        IEnumerator Shot(string name) { yield return new WaitForEndOfFrame(); Snap(p.transform.position.x, name); }

        p.transform.position = new Vector3(-6f, -0.08f, 0); rb.linearVelocity = Vector2.zero;
        p.scriptedMove = 0f; p.scriptedRun = true; yield return For(0.5f);

        // 1. crouch in place
        seen.Clear(); p.scriptedDown = true; yield return For(0.4f);
        sb.Append($"crouch: crouching={p.IsCrouching} collider h={col.size.y:F2} state={State()}\n");
        yield return Shot("crouch_idle");

        // 2. crouch walk
        float x0 = p.transform.position.x; p.scriptedMove = 1f; yield return For(0.4f);
        float xa = p.transform.position.x; yield return For(0.5f);
        float spd = (p.transform.position.x - xa) / 0.5f;
        yield return Shot("crouch_walk");
        sb.Append($"crouch walk: speed={spd:F2} u/s, states={string.Join(">", seen)}\n");

        // 3. ceiling at 2.0 (between crouch 1.75 and stand 2.55): release Down under it -> stays crouched
        var ceil = GameObject.CreatePrimitive(PrimitiveType.Quad); DestroyImmediate(ceil.GetComponent<MeshCollider>());
        float cx = p.transform.position.x + 0.9f;
        ceil.name = "TestCeiling"; ceil.transform.position = new Vector3(cx, 2.4f, 0); ceil.transform.localScale = new Vector3(1.6f, 0.8f, 1);
        ceil.AddComponent<BoxCollider2D>();
        yield return For(0.75f);   // walk under it
        var cb = col.bounds; var hit = Physics2D.OverlapBox(new Vector2(cb.center.x, cb.max.y + 0.4f), new Vector2(0.4f, 0.8f), 0f);
        string hitName = hit ? hit.name : "none";
        sb.Append($"  (before release: x={p.transform.position.x:F2} top={cb.max.y:F2} ceiling hit={hitName})\n");
        p.scriptedMove = 0f; p.scriptedDown = false; yield return For(0.3f);
        bool stayed = p.IsCrouching;
        yield return Shot("crouch_ceiling");
        p.scriptedMove = 1f; yield return For(1.2f); p.scriptedMove = 0f; yield return For(0.3f);
        sb.Append($"ceiling: under x={cx:F2}, released Down -> still crouching={stayed}; walked out -> crouching={p.IsCrouching} h={col.size.y:F2}\n");
        Destroy(ceil);

        // 4. run, slide with Down held -> ends in a crouch
        p.transform.position = new Vector3(-7f, -0.08f, 0); rb.linearVelocity = Vector2.zero; yield return For(0.3f);
        p.scriptedMove = 1f; yield return For(0.3f);
        seen.Clear(); p.scriptedDown = true; yield return null; yield return new WaitForFixedUpdate(); p.PressDash();
        p.scriptedMove = 0f; yield return For(1.0f);
        sb.Append($"slide -> crouch: crouching={p.IsCrouching} sliding={p.IsSliding} states={string.Join(">", seen)}\n");
        yield return Shot("crouch_after_slide");

        // 5. crouch -> slide (Shift while crouched)
        seen.Clear(); p.scriptedMove = -1f; yield return For(0.2f); p.PressDash(); yield return For(0.15f);
        bool slid = p.IsSliding; yield return For(0.9f);
        sb.Append($"crouch -> slide: started={slid}, then crouching={p.IsCrouching} states={string.Join(">", seen)}\n");
        p.scriptedDown = false; p.scriptedMove = 0f; yield return For(0.4f);
        sb.Append($"release: crouching={p.IsCrouching} state={State()}\n");

        p.scriptedMove = null;
        report = sb.ToString(); done = true;
    }
}
