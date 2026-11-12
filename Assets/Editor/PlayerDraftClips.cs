// Generates ROUGH Idle / Walk / Run bone-keyframe clips for Player.prefab and an Animator Controller.
// Every curve is a real Transform rotation/position key on a bone, editable in the Animation Window.
// Existing files are never overwritten: delete a clip (or the controller) yourself to regenerate it.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class PlayerDraftClips
{
    const string PrefabPath = "Assets/Character/Player.prefab";
    const string ClipDir = "Assets/Character/Animations/Draft";
    const string ControllerPath = "Assets/Character/Animations/Player.controller";

    // Sign convention (character faces +X): +deg = counter-clockwise.
    // Upper leg/arm +deg swings forward; lower leg -deg bends the knee; forearm +deg bends the elbow.
    // Limb parts are rigid (one bone each), so these are pure joint rotations, no mesh bending.
    delegate float Wave(float phase); // phase 0..1 over one cycle

    class Track { public string bone; public Wave rot; public Wave bobY; }

    static float S(float p, float offset = 0) => Mathf.Sin((p + offset) * Mathf.PI * 2f);
    static float Pos(float x) => Mathf.Max(0f, x);

    [MenuItem("Tools/Player Rig/Generate Draft Clips (skip existing)")]
    public static void Generate() => Generate(false);

    // Rewrites the curves of the existing *_Draft clips in place (same asset/GUID) from the CURRENT bind pose.
    // Use after moving joints in the Skinning Editor. Never touches clips that are not *_Draft.
    [MenuItem("Tools/Player Rig/Regenerate Draft Clips (overwrites *_Draft)")]
    public static void RegenerateDrafts() => Generate(true);

    static bool s_Overwrite;

    public static void Generate(bool overwriteDrafts)
    {
        s_Overwrite = overwriteDrafts;
        Directory.CreateDirectory(ClipDir);

        var idle = new List<Track>
        {
            new Track { bone = "spine", rot = p => 1.2f * S(p) },
            new Track { bone = "neck", rot = p => -0.6f * S(p, 0.1f) },
            new Track { bone = "head", rot = p => 1.0f * S(p, 0.15f) },
            new Track { bone = "arm_near_upper", rot = p => 1.5f * S(p, 0.1f) },
            new Track { bone = "arm_far_upper", rot = p => 1.5f * S(p, 0.1f) },
            new Track { bone = "hair_back_1", rot = p => 1.2f * S(p, 0.25f) },
            new Track { bone = "hair_back_2", rot = p => 1.5f * S(p, 0.35f) },
            new Track { bone = "hair_back_3", rot = p => 2.0f * S(p, 0.45f) },
            new Track { bone = "coat_back_1", rot = p => 0.8f * S(p, 0.3f) },
            new Track { bone = "coat_front_1", rot = p => -0.8f * S(p, 0.3f) },
            new Track { bone = "hip", bobY = p => -0.006f * (0.5f - 0.5f * S(p, 0.25f)) },
        };

        var walk = new List<Track>
        {
            new Track { bone = "leg_rear_upper", rot = p => 24f * S(p) },
            new Track { bone = "leg_front_upper", rot = p => -24f * S(p) },
            new Track { bone = "leg_rear_lower", rot = p => -35f * Pos(S(p, 0.3f)) - 4f },
            new Track { bone = "leg_front_lower", rot = p => -35f * Pos(-S(p, 0.3f)) - 4f },
            new Track { bone = "arm_near_upper", rot = p => -16f * S(p) },
            new Track { bone = "arm_far_upper", rot = p => 16f * S(p) },
            new Track { bone = "arm_near_fore", rot = p => 8f + 8f * Pos(-S(p)) },
            new Track { bone = "arm_far_fore", rot = p => 8f + 8f * Pos(S(p)) },
            new Track { bone = "spine", rot = p => -2f + 1f * S(p * 2f) },
            new Track { bone = "head", rot = p => 1f * S(p * 2f, 0.25f) },
            new Track { bone = "hair_back_1", rot = p => 2f + 2f * S(p * 2f, 0.2f) },
            new Track { bone = "hair_back_2", rot = p => 3f * S(p * 2f, 0.35f) },
            new Track { bone = "hair_back_3", rot = p => 4f * S(p * 2f, 0.5f) },
            new Track { bone = "coat_back_1", rot = p => -3f + 3f * S(p, 0.25f) },
            new Track { bone = "coat_front_1", rot = p => 3f * S(p, 0.25f) },
            new Track { bone = "skirt", rot = p => 2f * S(p * 2f, 0.2f) },
            new Track { bone = "hip", bobY = p => 0.012f * S(p * 2f, 0.25f) },
        };

        var run = new List<Track>
        {
            new Track { bone = "leg_rear_upper", rot = p => 38f * S(p) + 5f },
            new Track { bone = "leg_front_upper", rot = p => -38f * S(p) + 5f },
            new Track { bone = "leg_rear_lower", rot = p => -75f * Pos(S(p, 0.3f)) - 10f },
            new Track { bone = "leg_front_lower", rot = p => -75f * Pos(-S(p, 0.3f)) - 10f },
            new Track { bone = "arm_near_upper", rot = p => -35f * S(p) + 5f },
            new Track { bone = "arm_far_upper", rot = p => 35f * S(p) + 5f },
            new Track { bone = "arm_near_fore", rot = p => 70f + 10f * S(p) },
            new Track { bone = "arm_far_fore", rot = p => 70f - 10f * S(p) },
            new Track { bone = "spine", rot = p => -9f + 1.5f * S(p * 2f) },
            new Track { bone = "head", rot = p => 5f + 1f * S(p * 2f, 0.25f) },
            new Track { bone = "hair_back_1", rot = p => 8f + 3f * S(p * 2f, 0.2f) },
            new Track { bone = "hair_back_2", rot = p => 6f + 4f * S(p * 2f, 0.35f) },
            new Track { bone = "hair_back_3", rot = p => 6f + 5f * S(p * 2f, 0.5f) },
            new Track { bone = "coat_back_1", rot = p => 10f + 4f * S(p * 2f, 0.25f) },
            new Track { bone = "coat_back_2", rot = p => 6f + 4f * S(p * 2f, 0.4f) },
            new Track { bone = "coat_front_1", rot = p => 6f + 3f * S(p * 2f, 0.25f) },
            new Track { bone = "skirt", rot = p => 4f + 3f * S(p * 2f, 0.2f) },
            new Track { bone = "hip", bobY = p => 0.03f * S(p * 2f, 0.25f) - 0.02f },
        };

        var contents = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var animator = contents.GetComponentInChildren<Animator>(true);
            var animRoot = animator.transform;
            var clips = new Dictionary<string, AnimationClip>
            {
                ["Idle"] = MakeClip("Player_Idle_Draft", animRoot, idle, 2.0f, 8),
                ["Walk"] = MakeClip("Player_Walk_Draft", animRoot, walk, 1.0f, 16),
                ["Run"] = MakeClip("Player_Run_Draft", animRoot, run, 0.6f, 16),
            };

            if (!File.Exists(ControllerPath))
            {
                var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
                ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
                var sm = ctrl.layers[0].stateMachine;
                var sIdle = sm.AddState("Idle"); sIdle.motion = clips["Idle"];
                var sWalk = sm.AddState("Walk"); sWalk.motion = clips["Walk"];
                var sRun = sm.AddState("Run"); sRun.motion = clips["Run"];
                sm.defaultState = sIdle;
                void T(AnimatorState a, AnimatorState b, AnimatorConditionMode m, float v)
                {
                    var t = a.AddTransition(b); t.hasExitTime = false; t.duration = 0.12f; t.AddCondition(m, v, "Speed");
                }
                T(sIdle, sWalk, AnimatorConditionMode.Greater, 0.1f);
                T(sWalk, sIdle, AnimatorConditionMode.Less, 0.1f);
                T(sWalk, sRun, AnimatorConditionMode.Greater, 3.5f);
                T(sRun, sWalk, AnimatorConditionMode.Less, 3.5f);
                T(sIdle, sRun, AnimatorConditionMode.Greater, 3.5f);
                Debug.Log("[PlayerDraftClips] Created " + ControllerPath);
            }
            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
            if (animator.runtimeAnimatorController == null)
            {
                animator.runtimeAnimatorController = controller;
                PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
            }
        }
        finally { PrefabUtility.UnloadPrefabContents(contents); }
        AssetDatabase.SaveAssets();
    }

    static AnimationClip MakeClip(string name, Transform animRoot, List<Track> tracks, float duration, int samples)
    {
        string path = $"{ClipDir}/{name}.anim";
        var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (existing != null && !s_Overwrite) { Debug.Log("[PlayerDraftClips] Kept existing " + path); return existing; }

        var clip = existing ?? new AnimationClip { name = name, frameRate = 30 };
        clip.ClearCurves();
        var bones = animRoot.GetComponentsInChildren<Transform>(true);
        foreach (var tr in tracks)
        {
            var bone = bones.FirstOrDefault(b => b.name == tr.bone);
            if (bone == null) throw new Exception("Bone not found: " + tr.bone);
            string bpath = AnimationUtility.CalculateTransformPath(bone, animRoot);
            if (tr.rot != null)
            {
                Vector3 e = bone.localEulerAngles;
                Set(clip, bpath, "localEulerAnglesRaw.x", duration, samples, _ => e.x);
                Set(clip, bpath, "localEulerAnglesRaw.y", duration, samples, _ => e.y);
                Set(clip, bpath, "localEulerAnglesRaw.z", duration, samples, p => e.z + tr.rot(p));
            }
            if (tr.bobY != null)
            {
                Vector3 lp = bone.localPosition;
                Vector3 Local(float dy) => lp + bone.parent.InverseTransformVector(new Vector3(0, dy, 0));
                Set(clip, bpath, "m_LocalPosition.x", duration, samples, p => Local(tr.bobY(p)).x);
                Set(clip, bpath, "m_LocalPosition.y", duration, samples, p => Local(tr.bobY(p)).y);
                Set(clip, bpath, "m_LocalPosition.z", duration, samples, _ => lp.z);
            }
        }
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        if (existing == null) AssetDatabase.CreateAsset(clip, path); else EditorUtility.SetDirty(clip);
        Debug.Log("[PlayerDraftClips] Created " + path);
        return clip;
    }

    static void Set(AnimationClip clip, string path, string prop, float duration, int samples, Func<float, float> f)
    {
        var keys = new Keyframe[samples + 1];
        for (int i = 0; i <= samples; ++i)
        {
            float p = i / (float)samples;
            keys[i] = new Keyframe(p * duration, f(i == samples ? 0f : p)); // last key == first key for a clean loop
        }
        var curve = new AnimationCurve(keys);
        for (int i = 0; i < keys.Length; ++i) curve.SmoothTangents(i, 0f);
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), prop), curve);
    }
}
