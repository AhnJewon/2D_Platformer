using UnityEngine;

// Test helper: holds a character at a fixed time of a clip (used to line up key poses side by side).
public class ClipPoseSampler : MonoBehaviour
{
    public AnimationClip clip;
    [Range(0f, 1f)] public float normalizedTime;

    void Start()
    {
        var animator = GetComponentInChildren<Animator>();
        if (animator) animator.enabled = false;
    }

    void LateUpdate()
    {
        if (clip == null) return;
        var target = GetComponentInChildren<Animator>(true);
        clip.SampleAnimation(target ? target.gameObject : gameObject, normalizedTime * clip.length);
    }
}
