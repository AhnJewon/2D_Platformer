using System.Collections;
using System.IO;
using UnityEngine;

// Dev tool: simulates platformer jumps (standing and running) with simple jump physics, drives the REAL Animator
// parameters every frame (Speed, Jump, Grounded, VelY, AirVel) and writes one PNG per frame.
// Time.captureFramerate fixes the frame time, so the result is the same as the game would show at that frame rate.
public class JumpSimRecorder : MonoBehaviour
{
    public Transform player;
    public int fps = 30;
    public int width = 520, height = 900;
    public float takeoffSpeed = 5.5f;   // units/s
    public float riseGravity = 13.75f;  // -> apex ~1.1 units after 0.4 s
    public float fallMultiplier = 1.6f; // faster fall (game-feel convention)
    public string outputFolder = "Screenshots/Recordings";
    public bool done;

    IEnumerator Start()
    {
        Time.captureFramerate = fps;
        var anim = player.GetComponentInChildren<Animator>();
        var cam = GetComponent<Camera>();
        var rt = new RenderTexture(width, height, 24); cam.targetTexture = rt;
        var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
        string root = Path.Combine(Path.GetDirectoryName(Application.dataPath), outputFolder);
        foreach (var (name, speed) in new[] { ("JumpSim_Stand", 0f), ("JumpSim_Run", 4.5f) })
        {
            string dir = Path.Combine(root, name); Directory.CreateDirectory(dir);
            foreach (var f in Directory.GetFiles(dir, "*.png")) File.Delete(f);
            float y = 0f, vy = 0f; bool grounded = true; int frame = 0;
            anim.Rebind(); anim.SetFloat("Speed", speed); anim.SetBool("Grounded", true);
            player.position = new Vector3(0, 0, 0);
            int pre = Mathf.RoundToInt(0.5f * fps), post = Mathf.RoundToInt(0.7f * fps), landedAt = -1;
            for (int i = 0; ; ++i)
            {
                float dt = 1f / fps;
                if (i == pre) { anim.SetTrigger("Jump"); vy = takeoffSpeed; grounded = false; }
                else if (!grounded)
                {
                    vy -= riseGravity * (vy > 0 ? 1f : fallMultiplier) * dt;
                    y += vy * dt;
                    if (y <= 0f) { y = 0f; vy = 0f; grounded = true; landedAt = i; }
                }
                player.position = new Vector3(0, y, 0);
                anim.SetFloat("Speed", speed);
                anim.SetBool("Grounded", grounded);
                anim.SetFloat("VelY", vy);
                anim.SetFloat("AirVel", Mathf.Clamp(vy / takeoffSpeed, -1f, 1f));
                yield return new WaitForEndOfFrame();
                RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, width, height), 0, 0); tex.Apply(); RenderTexture.active = null;
                File.WriteAllBytes(Path.Combine(dir, $"{frame++:000}.png"), tex.EncodeToPNG());
                if (landedAt >= 0 && i >= landedAt + post) break;
            }
        }
        cam.targetTexture = null; Time.captureFramerate = 0;
        done = true;
    }
}
