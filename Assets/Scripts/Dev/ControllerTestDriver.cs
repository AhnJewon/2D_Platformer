using System.Collections;
using System.Text;
using UnityEngine;

// Dev tool: drives PlayerController2D with scripted input (walk, run, jump onto a platform, run-jump over the gap)
// and records what happened (positions, grounded, Animator states) into `report`.
public class ControllerTestDriver : MonoBehaviour
{
    public PlayerController2D player;
    public string report = "";
    public bool done;

    IEnumerator Start()
    {
        if (!player) player = FindAnyObjectByType<PlayerController2D>();
        var anim = player.GetComponentInChildren<Animator>();
        var sb = new StringBuilder();
        string State() { var s = anim.GetCurrentAnimatorStateInfo(0); foreach (var n in new[] { "Idle", "Walk", "Run", "JumpStart", "Air", "Land" }) if (s.IsName(n)) return n; return "?"; }
        IEnumerator For(float sec, System.Action each = null) { float t = 0; while (t < sec) { each?.Invoke(); t += Time.deltaTime; yield return null; } }

        yield return For(0.6f);
        sb.Append($"start: pos={player.transform.position.x:F2},{player.transform.position.y:F2} grounded={player.IsGrounded} state={State()}\n");

        player.scriptedMove = 1f; player.scriptedRun = false;
        yield return For(1.0f);
        sb.Append($"walk 1s: x={player.transform.position.x:F2} vx={player.GetComponent<Rigidbody2D>().linearVelocity.x:F2} state={State()}\n");

        // jump onto Platform_1 (top 1.1, x -1.5..1.5)
        float maxY = -9f; var seen = new System.Collections.Generic.List<string>();
        player.PressJump(true);
        yield return For(1.4f, () => { if (Time.time > 0 && player.transform.position.y > 1.2f) player.scriptedJumpHeld = false; maxY = Mathf.Max(maxY, player.transform.position.y); var s = State(); if (seen.Count == 0 || seen[seen.Count - 1] != s) seen.Add(s); });
        sb.Append($"jump: maxY={maxY:F2} now y={player.transform.position.y:F2} grounded={player.IsGrounded} states={string.Join(">", seen)}\n");

        player.scriptedMove = 0f;
        yield return For(0.5f);
        sb.Append($"stop: x={player.transform.position.x:F2} y={player.transform.position.y:F2} state={State()}\n");

        // run right off the platform and jump the gap (x 4..6.5)
        player.scriptedMove = 1f; player.scriptedRun = true; seen.Clear();
        bool jumped = false; float t2 = 0;
        while (t2 < 3.5f)
        {
            var s = State(); if (seen.Count == 0 || seen[seen.Count - 1] != s) seen.Add(s);
            if (!jumped && player.IsGrounded && player.transform.position.x > 3.0f) { player.PressJump(true); jumped = true; }
            t2 += Time.deltaTime; yield return null;
        }
        sb.Append($"run+gap: x={player.transform.position.x:F2} y={player.transform.position.y:F2} grounded={player.IsGrounded} states={string.Join(">", seen)}\n");
        player.scriptedMove = null;
        report = sb.ToString(); done = true;
    }
}
