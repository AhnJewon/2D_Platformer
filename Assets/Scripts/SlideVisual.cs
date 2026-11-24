using UnityEngine;

// Swaps the rigged character for the hand-drawn slide loop while sliding.
// Put it on the Player; it creates a "SlideSprite" child under the model (so it flips with her facing).
public class SlideVisual : MonoBehaviour
{
    public Sprite[] frames;          // Assets/Characters/Officer/Slide/Slide_00..03
    public float fps = 12f;
    public int sortingOrder = 30;    // drawn like the rig (rig parts use 0..29)

    SpriteRenderer slide;
    public SpriteRenderer Renderer => slide;          // DashFX copies it for the slide afterimages
    public bool IsShowing => showing && slide != null && slide.enabled;
    SpriteRenderer[] rig;
    bool showing;
    float t;

    void Awake()
    {
        var anim = GetComponentInChildren<Animator>();
        var parent = anim ? anim.transform : transform;
        rig = parent.GetComponentsInChildren<SpriteRenderer>(true);
        var go = new GameObject("SlideSprite");
        go.transform.SetParent(parent, false);
        slide = go.AddComponent<SpriteRenderer>();
        slide.sortingOrder = sortingOrder;
        slide.enabled = false;
    }

    public void Show(bool on)
    {
        if (on == showing) return;
        showing = on; t = 0f;
        slide.enabled = on && frames != null && frames.Length > 0;
        foreach (var r in rig) if (r) r.enabled = !slide.enabled;
    }

    void Update()
    {
        if (!showing || frames == null || frames.Length == 0) return;
        t += Time.deltaTime;
        slide.sprite = frames[(int)(t * fps) % frames.Length];
    }
}
