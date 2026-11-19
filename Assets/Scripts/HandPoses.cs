using System;
using UnityEngine;

// Swappable hand drawings on top of the rig. The rig's own hands (13_hand_near / 10c_arm_far_hand) are skinned fists;
// a pose replaces one with a plain sprite on the same bone, so the hand can open, spread or grip differently as needed.
// Art: NovelAI v4.5 gloves cut by Tools/import_hands.py (pivot at the wrist, rig scale). Each pose stores the angle of
// its wrist->tip axis inside the sprite; the drawn rig fists hang straight down (-90 deg) in the bind pose.
// Usage (after the bones are final for the frame, before rendering):
//   Set(Side.Near, "GripInline", axisDeg)  - show that pose with its wrist->tip axis at axisDeg (model space)
//   Set(Side.Far, null)                    - back to the rig's own drawn hand
[DefaultExecutionOrder(-40)]   // after PlayerCombat (-50); plain sprites, so only the final bone transform matters
public class HandPoses : MonoBehaviour
{
    public enum Side { Near, Far }

    [Serializable] public class Pose
    {
        public string name;
        public Sprite sprite;
        public float spriteAxis;    // wrist->tip angle inside the sprite (deg, +X = 0)
    }
    public Pose[] poses;

    class Hand
    {
        public Transform bone; public SpriteRenderer rig, swap;
        public float bindBoneDir;   // bone axis in the bind pose (model space)
        public Pose pose; public float axis; public bool axisSet;
    }
    readonly Hand[] hands = new Hand[2];
    Transform model;

    void Awake()
    {
        var anim = GetComponentInChildren<Animator>();
        var pc = GetComponent<PlayerController2D>();
        model = pc != null && pc.model != null ? pc.model : anim.transform;
        Transform Find(string n) { foreach (var t in GetComponentsInChildren<Transform>(true)) if (t.name == n) return t; return null; }
        string[] bones = { "arm_near_hand", "arm_far_hand" }, rigs = { "13_hand_near", "10c_arm_far_hand" };
        for (int i = 0; i < 2; ++i)
        {
            var h = new Hand { bone = Find(bones[i]) };
            var r = Find(rigs[i]); if (r != null) h.rig = r.GetComponent<SpriteRenderer>();
            if (h.bone == null || h.rig == null) continue;
            h.bindBoneDir = BoneDir(h.bone);
            var go = new GameObject("HandPose_" + (Side)i); go.transform.SetParent(model, false);
            h.swap = go.AddComponent<SpriteRenderer>(); h.swap.sortingOrder = h.rig.sortingOrder; h.swap.enabled = false;
            hands[i] = h;
        }
    }

    Vector2 M(Transform t, Vector3 local) => model.InverseTransformPoint(t.TransformPoint(local));
    float BoneDir(Transform b) { var d = M(b, Vector3.right * 0.1f) - M(b, Vector3.zero); return Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg; }

    // the drawn hand's wrist->tip axis that the bone currently gives (the rig fists hang at -90 in the bind pose)
    public float RigAxis(Side side)
    {
        var h = hands[(int)side]; if (h == null) return -90f;
        return BoneDir(h.bone) - h.bindBoneDir - 90f;
    }

    public Pose Find(string name) { if (poses != null) foreach (var p in poses) if (p != null && p.name == name) return p; return null; }

    // pose = null: the rig's own hand. axis = wrist->tip angle in model space (default: follow the bone).
    public void Set(Side side, string pose, float? axis = null)
    {
        var h = hands[(int)side]; if (h == null) return;
        h.pose = string.IsNullOrEmpty(pose) ? null : Find(pose);
        h.axisSet = axis.HasValue; h.axis = axis ?? 0f;
    }

    void LateUpdate()
    {
        foreach (var h in hands)
        {
            if (h == null) continue;
            bool on = h.pose != null && h.pose.sprite != null;
            h.swap.enabled = on;
            if (!on) continue;          // the rig hand's own visibility is left alone (SlideVisual re-enables it every Update)
            h.rig.enabled = false;
            float axis = h.axisSet ? h.axis : BoneDir(h.bone) - h.bindBoneDir - 90f;
            Vector2 wrist = M(h.bone, Vector3.zero);
            h.swap.sprite = h.pose.sprite;
            h.swap.transform.localPosition = new Vector3(wrist.x, wrist.y, -0.0005f);
            h.swap.transform.localRotation = Quaternion.Euler(0, 0, axis - h.pose.spriteAxis);
        }
    }
}
