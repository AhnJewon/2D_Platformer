using System.Collections;
using System.IO;
using UnityEngine;

// Dev tool: in Play mode, steps a clip frame by frame on `target` and writes PNGs (for preview GIFs).
// Each clip is sampled at exact times (frame i / fps), so the loop is seamless and no frames are skipped.
public class ClipRecorder : MonoBehaviour
{
    public GameObject target;            // the Player instance (its Animator is disabled while recording)
    public AnimationClip[] clips;
    public int fps = 30;
    public int width = 520, height = 760;
    public string outputFolder = "Screenshots/Recordings";  // relative to the project root
    public bool done;

    IEnumerator Start()
    {
        var animator = target.GetComponentInChildren<Animator>(true);
        if (animator) animator.enabled = false;
        var cam = GetComponent<Camera>();
        var rt = new RenderTexture(width, height, 24);
        cam.targetTexture = rt;
        var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
        string root = Path.Combine(Path.GetDirectoryName(Application.dataPath), outputFolder);
        foreach (var clip in clips)
        {
            string dir = Path.Combine(root, clip.name);
            Directory.CreateDirectory(dir);
            foreach (var f in Directory.GetFiles(dir, "*.png")) File.Delete(f);
            int frames = Mathf.Max(1, Mathf.RoundToInt(clip.length * fps));
            for (int i = 0; i < frames; ++i)
            {
                clip.SampleAnimation(animator ? animator.gameObject : target, i / (float)fps);
                yield return new WaitForEndOfFrame();   // Sprite Skin deforms in LateUpdate, camera renders after
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                File.WriteAllBytes(Path.Combine(dir, $"{i:000}.png"), tex.EncodeToPNG());
            }
        }
        cam.targetTexture = null;
        done = true;
    }
}
