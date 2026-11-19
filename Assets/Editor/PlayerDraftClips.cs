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

    // Periodic key curve: Keys(phase0, value0, phase1, value1, ...) with phases in [0,1), Catmull-Rom between keys.
    static Wave Keys(params float[] pv)
    {
        int n = pv.Length / 2;
        float[] t = new float[n], v = new float[n];
        for (int i = 0; i < n; ++i) { t[i] = pv[2 * i]; v[i] = pv[2 * i + 1]; }
        return p =>
        {
            p = ((p % 1f) + 1f) % 1f;
            int k = n - 1;
            for (int i = 0; i < n; ++i) if (t[i] <= p) k = i;
            int k1 = (k + 1) % n, k0 = (k - 1 + n) % n, k2 = (k + 2) % n;
            float span = (t[k1] - t[k] + 1f) % 1f; if (span <= 0f) span = 1f;
            float u = ((p - t[k] + 1f) % 1f) / span;
            float a = v[k0], b = v[k], c = v[k1], d = v[k2];
            return 0.5f * (2f * b + (-a + c) * u + (2f * a - 5f * b + 4f * c - d) * u * u + (-a + 3f * b - 3f * c + d) * u * u * u);
        };
    }

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

        // Joint curves follow gait references (draft values, tune in the Animation Window):
        //  walk: hip ~ +28 flexion / -12 extension, knee ~15 deg at loading, ~60 deg peak in swing
        //  run:  hip ~ +50 / -15, knee ~40 deg in stance, ~105 deg peak in swing, trunk leaning forward ~15 deg
        // Leg phase 0 = that leg's heel contact. The near arm (drawn on top) swings with the FAR (rear) leg,
        // the far arm with the near (front) leg, as in a real gait.
        Wave walkHip = Keys(0f, 26f, .12f, 20f, .3f, 6f, .5f, -12f, .6f, -8f, .75f, 14f, .9f, 28f);
        Wave walkKnee = Keys(0f, 3f, .12f, 15f, .3f, 7f, .48f, 6f, .6f, 38f, .72f, 60f, .85f, 28f, .95f, 6f);
        Wave runHip = Keys(0f, 30f, .15f, 12f, .35f, -15f, .45f, -8f, .62f, 30f, .8f, 50f, .92f, 40f);
        Wave runKnee = Keys(0f, 18f, .12f, 40f, .33f, 22f, .5f, 85f, .65f, 105f, .8f, 55f, .93f, 22f);
        Wave Shift(Wave w, float d) => p => w((p + d) % 1f);
        // Toe bend at push-off: thick-soled boots only flex a little, so the toe cap takes ~1/3 of the shin tilt
        // (shin angle = hip - knee), capped at 12 deg walk / 14 deg run, only inside the push-off window [a..d].
        // Full counter-rotation (toe flat on the ground) folded the boot so much that the foot looked short.
        float Win(float p, float a, float b, float c, float d) => p < a || p > d ? 0f : p < b ? Mathf.SmoothStep(0f, 1f, (p - a) / (b - a)) : p > c ? Mathf.SmoothStep(1f, 0f, (p - c) / (d - c)) : 1f;
        Wave WalkToe(float d) => p => { float q = (p + d) % 1f; return Mathf.Clamp(0.35f * (walkKnee(q) - walkHip(q)), 0f, 12f) * Win(q, .42f, .5f, .6f, .68f); };
        Wave RunToe(float d) => p => { float q = (p + d) % 1f; return Mathf.Clamp(0.35f * (runKnee(q) - runHip(q)), 0f, 14f) * Win(q, .2f, .26f, .34f, .42f); };

        var idle = new List<Track>
        {
            // The drawing leans the waist back ~9 deg; idle straightens it 4 deg (negative = forward),
            // and neck/head/arms take it back so the face and hands keep their drawn direction.
            new Track { bone = "spine", rot = p => -4f + 1.2f * S(p) },
            new Track { bone = "neck", rot = p => 2f - 0.6f * S(p, 0.1f) },
            new Track { bone = "head", rot = p => 2f + 1.0f * S(p, 0.15f) },
            new Track { bone = "arm_near_upper", rot = p => 4f + 1.5f * S(p, 0.1f) },
            new Track { bone = "arm_far_upper", rot = p => 4f + 1.5f * S(p, 0.1f) },
            new Track { bone = "hair_back_1", rot = p => 1.2f * S(p, 0.25f) },
            new Track { bone = "hair_back_2", rot = p => 1.5f * S(p, 0.35f) },
            new Track { bone = "hair_back_3", rot = p => 2.0f * S(p, 0.45f) },
            new Track { bone = "coat_back_1", rot = p => 0.8f * S(p, 0.3f) },
            new Track { bone = "coat_front_1", rot = p => -0.8f * S(p, 0.3f) },
            new Track { bone = "hip", bobY = p => -0.006f * (0.5f - 0.5f * S(p, 0.25f)) },
        };

        var walk = new List<Track>
        {
            new Track { bone = "leg_rear_upper", rot = walkHip },
            new Track { bone = "leg_rear_lower", rot = p => -walkKnee(p) },
            new Track { bone = "leg_front_upper", rot = Shift(walkHip, 0.5f) },
            new Track { bone = "leg_front_lower", rot = p => -walkKnee((p + 0.5f) % 1f) },
            new Track { bone = "toe_rear", rot = WalkToe(0f) },
            new Track { bone = "toe_front", rot = WalkToe(0.5f) },
            new Track { bone = "arm_near_upper", rot = p => 16f * S(p, 0.2f) },
            new Track { bone = "arm_far_upper", rot = p => -16f * S(p, 0.2f) },
            new Track { bone = "arm_near_fore", rot = p => 10f + 10f * Pos(S(p, 0.2f)) },
            new Track { bone = "arm_far_fore", rot = p => 10f + 10f * Pos(-S(p, 0.2f)) },
            new Track { bone = "spine", rot = p => -3f + 1f * S(p * 2f, 0.1f) },
            new Track { bone = "head", rot = p => 2f + 1f * S(p * 2f, 0.35f) },
            new Track { bone = "hair_back_1", rot = p => -3f + 2f * S(p * 2f, 0.2f) },
            new Track { bone = "hair_back_2", rot = p => -2f + 3f * S(p * 2f, 0.35f) },
            new Track { bone = "hair_back_3", rot = p => -2f + 4f * S(p * 2f, 0.5f) },
            new Track { bone = "coat_back_1", rot = p => -3f + 3f * S(p, 0.25f) },
            new Track { bone = "coat_front_1", rot = p => -2f + 3f * S(p, 0.25f) },
            new Track { bone = "skirt", rot = p => -1f + 2f * S(p * 2f, 0.2f) },
            // lowest just after each heel contact, highest at passing
            new Track { bone = "hip", bobY = p => -0.009f * Mathf.Cos((p - 0.12f) * Mathf.PI * 4f) },
        };

        var run = new List<Track>
        {
            // pelvis tilts forward 7 deg (coat/skirt lean with it); thighs take it back so the stride stays the same
            new Track { bone = "hip", rot = p => -7f },
            new Track { bone = "leg_rear_upper", rot = p => runHip(p) + 7f },
            new Track { bone = "leg_rear_lower", rot = p => -runKnee(p) },
            new Track { bone = "leg_front_upper", rot = p => runHip((p + 0.5f) % 1f) + 7f },
            new Track { bone = "leg_front_lower", rot = p => -runKnee((p + 0.5f) % 1f) },
            new Track { bone = "toe_rear", rot = RunToe(0f) },
            new Track { bone = "toe_front", rot = RunToe(0.5f) },
            new Track { bone = "arm_near_upper", rot = p => 5f + 38f * S(p, 0.15f) },
            new Track { bone = "arm_far_upper", rot = p => 5f - 38f * S(p, 0.15f) },
            new Track { bone = "arm_near_fore", rot = p => 85f + 10f * S(p, 0.15f) },
            new Track { bone = "arm_far_fore", rot = p => 85f - 10f * S(p, 0.15f) },
            // trunk leans forward; head compensates so she still looks ahead
            new Track { bone = "spine", rot = p => -12f + 1.5f * S(p * 2f, 0.1f) },
            new Track { bone = "neck", rot = p => 5f },
            new Track { bone = "head", rot = p => 10f + 1f * S(p * 2f, 0.35f) },
            // Hanging things trail BEHIND her while running: negative = clockwise = tip swings back (-X).
            // Hair is under spine(-16)+neck(+5)+head(+7), so hair_back_1 -12 nets about -16 deg at the root.
            new Track { bone = "hair_back_1", rot = p => -12f + 3f * S(p * 2f, 0.2f) },
            new Track { bone = "hair_back_2", rot = p => -9f + 4f * S(p * 2f, 0.35f) },
            new Track { bone = "hair_back_3", rot = p => -9f + 5f * S(p * 2f, 0.5f) },
            new Track { bone = "hair_front_near_1", rot = p => -6f + 2f * S(p * 2f, 0.3f) },
            new Track { bone = "hair_front_far_1", rot = p => -6f + 2f * S(p * 2f, 0.3f) },
            new Track { bone = "coat_back_1", rot = p => -14f + 4f * S(p * 2f, 0.25f) },
            new Track { bone = "coat_back_2", rot = p => -8f + 4f * S(p * 2f, 0.4f) },
            new Track { bone = "coat_front_1", rot = p => -10f + 3f * S(p * 2f, 0.25f) },
            new Track { bone = "coat_front_2", rot = p => -6f + 3f * S(p * 2f, 0.4f) },
            new Track { bone = "skirt", rot = p => -5f + 3f * S(p * 2f, 0.2f) },
            // low on landing (stance), high in the flight phase
            new Track { bone = "hip", bobY = p => 0.02f * Mathf.Cos((p - 0.42f) * Mathf.PI * 4f) - 0.015f },
        };

        var contents = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var animator = contents.GetComponentInChildren<Animator>(true);
            var animRoot = animator.transform;
            var clips = new Dictionary<string, AnimationClip>
            {
                ["Idle"] = MakeClip("Player_Idle_Draft", animRoot, idle, 2.0f, 8),
                ["Walk"] = MakeClip("Player_Walk_Draft", animRoot, walk, 1.0f, 24),
                ["Run"] = MakeClip("Player_Run_Draft", animRoot, run, 0.6f, 20),
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
            var jumpClips = MakeJumpClips(animRoot);
            EnsureJumpStates(AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath), jumpClips);
            EnsureDashSlideStates(AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath), MakeDashSlideClips(animRoot));
            EnsureCrouchStates(AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath), MakeCrouchClips(animRoot));
            EnsureBackpedalState(AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath), MakeBackpedalClip(animRoot));
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


    // ---------------- Jump (draft) ----------------
    // Key poses: anticipation crouch -> take-off -> rise -> apex tuck -> descent (legs reach forward, arms back,
    // hair/coat lifted by the air) -> impact crouch -> idle. Angles come from ArtSource/references/jump_reference.md.
    // Take-off is kept to 0.1 s so the jump still feels instant to the player. Values are offsets from the bind
    // pose in degrees (same sign convention as above); "bob" is the hip's vertical offset in world units.
    // Ground poses (crouch/landing) get their hip height solved so the lower foot stays on the floor.
    class Pose : Dictionary<string, float> { public float bob; }

    static Pose P(float bob = 0f, params (string bone, float deg)[] r)
    {
        var p = new Pose { bob = bob };
        foreach (var (b, d) in r) p[b] = d;
        return p;
    }

    // smooth (non-looping) interpolation through timed poses
    static List<Track> PoseTracks((float t, Pose pose)[] keys, Dictionary<string, Wave> extra = null, bool solveBob = false, Transform animRoot = null)
    {
        var bones = keys.SelectMany(k => k.pose.Keys).Distinct().ToList();
        float Interp(float p, Func<Pose, float> get)
        {
            if (p <= keys[0].t) return get(keys[0].pose);
            for (int i = 0; i < keys.Length - 1; ++i)
                if (p <= keys[i + 1].t)
                {
                    float u = Mathf.SmoothStep(0f, 1f, (p - keys[i].t) / (keys[i + 1].t - keys[i].t));
                    return Mathf.Lerp(get(keys[i].pose), get(keys[i + 1].pose), u);
                }
            return get(keys[keys.Length - 1].pose);
        }
        var tracks = new List<Track>();
        foreach (var b in bones)
        {
            string bone = b;
            Wave baseW = p => Interp(p, pose => pose.TryGetValue(bone, out var v) ? v : 0f);
            Wave w = baseW;
            if (extra != null && extra.TryGetValue(bone, out var ex)) w = p => baseW(p) + ex(p);
            tracks.Add(new Track { bone = bone, rot = w });
        }
        if (extra != null)
            foreach (var kv in extra.Where(kv => !bones.Contains(kv.Key)))
                tracks.Add(new Track { bone = kv.Key, rot = kv.Value });
        Wave bob = p => Interp(p, pose => pose.bob);
        if (solveBob && animRoot != null)
        {
            // per key: lower the hip by however much the bent legs lifted the lower foot
            var solved = keys.Select(k => (k.t, k.pose, drop: FootLift(animRoot, k.pose))).ToArray();
            bob = p => Interp(p, pose => pose.bob - solved.First(s => s.pose == pose).drop);
        }
        tracks.Add(new Track { bone = "hip", bobY = bob });
        return tracks;
    }

    // How much higher (world units) the lower foot ends up when the pose's leg angles are applied.
    static float FootLift(Transform animRoot, Pose pose)
    {
        var all = animRoot.GetComponentsInChildren<Transform>(true);
        Transform T(string n) => all.First(t => t.name == n);
        string[] legBones = { "hip", "leg_rear_upper", "leg_rear_lower", "leg_front_upper", "leg_front_lower", "foot_rear", "foot_front", "toe_rear", "toe_front" };
        float Lowest() => Mathf.Min(T("foot_rear").position.y, T("foot_front").position.y);  // ankles
        float before = Lowest();
        var saved = legBones.ToDictionary(n => n, n => T(n).localRotation);
        foreach (var n in legBones) if (pose.TryGetValue(n, out var d)) T(n).localRotation *= Quaternion.Euler(0, 0, d);
        float after = Lowest();
        foreach (var kv in saved) T(kv.Key).localRotation = kv.Value;
        return after - before;
    }

    static float Flutter(float p, float amp, float phase, float cycles = 1f) => amp * Mathf.Sin((p * cycles + phase) * Mathf.PI * 2f);

    // Foot counter-rotates against thigh+shin so the sole stays flat on the floor (needs the foot_* ankle bones).
    static Pose Flat(Pose p)
    {
        foreach (var side in new[] { "rear", "front" })
        {
            float h = p.TryGetValue("hip", out var hh) ? hh : 0f;
            float t = p.TryGetValue("leg_" + side + "_upper", out var a) ? a : 0f;
            float s = p.TryGetValue("leg_" + side + "_lower", out var b) ? b : 0f;
            p["foot_" + side] = -(h + t + s);
        }
        return p;
    }

    // Whole-body lean: tilts the pelvis (hip bone, which also carries the coat and skirt) and sets the chest bend.
    // keepLegs = on the ground: the thighs take the tilt back so the legs/feet keep their place.
    static Pose Lean(Pose p, float hip, float spine, bool keepLegs)
    {
        p["hip"] = hip; p["spine"] = spine;
        if (keepLegs)
            foreach (var side in new[] { "rear", "front" })
                p["leg_" + side + "_upper"] = (p.TryGetValue("leg_" + side + "_upper", out var t) ? t : 0f) - hip;
        return p;
    }

    // Poses: ArtSource/references/jump_reference.md (Toon Boom vertical jump = standing set, Muybridge running broad jump
    // = moving set). Air poses are picked by the Animator from the real vertical speed (AirVel) and blended with Speed.
    static Dictionary<string, AnimationClip> MakeJumpClips(Transform animRoot)
    {
        // ---------- shared ground poses ----------
        var idle0 = Flat(P(0f, ("spine", -4f), ("neck", 2f), ("head", 2f), ("arm_near_upper", 4f), ("arm_far_upper", 4f),
            ("leg_rear_upper", 0f), ("leg_rear_lower", 0f), ("leg_front_upper", 0f), ("leg_front_lower", 0f), ("toe_rear", 0f), ("toe_front", 0f)));
        var crouch = Flat(P(0f, ("leg_rear_upper", 55f), ("leg_rear_lower", -95f), ("leg_front_upper", 55f), ("leg_front_lower", -95f),
            ("spine", -30f), ("neck", 8f), ("head", 14f),
            ("arm_near_upper", -25f), ("arm_far_upper", -25f), ("arm_near_fore", 10f), ("arm_far_fore", 10f),
            ("hair_back_1", 4f), ("coat_back_1", 4f), ("coat_front_1", -3f), ("skirt", 2f)));
        var impact = Flat(P(0f, ("leg_rear_upper", 55f), ("leg_rear_lower", -100f), ("leg_front_upper", 55f), ("leg_front_lower", -100f),
            ("spine", -28f), ("neck", 8f), ("head", 14f),
            ("arm_near_upper", 30f), ("arm_far_upper", 30f), ("arm_near_fore", 25f), ("arm_far_fore", 25f),
            ("hair_back_1", 8f), ("hair_back_2", 6f), ("hair_back_3", 6f), ("coat_back_1", 7f), ("coat_front_1", -5f), ("skirt", -4f)));

        // ---------- standing air set (Toon Boom 4-7) ----------
        var sRise = P(0f, ("leg_rear_upper", -4f), ("leg_rear_lower", -6f), ("foot_rear", -25f), ("leg_front_upper", -2f), ("leg_front_lower", -6f), ("foot_front", -25f),
            ("spine", 2f), ("neck", 0f), ("head", 4f),
            ("arm_near_upper", 140f), ("arm_near_fore", 10f), ("arm_far_upper", 125f), ("arm_far_fore", 10f),
            ("hair_back_1", 5f), ("hair_back_2", 3f), ("coat_back_1", 5f), ("coat_front_1", -4f), ("skirt", -2f));
        var sApex = P(0f, ("leg_rear_upper", 62f), ("leg_rear_lower", -100f), ("foot_rear", -10f), ("leg_front_upper", 68f), ("leg_front_lower", -100f), ("foot_front", -10f),
            ("spine", -8f), ("neck", 3f), ("head", 5f),
            ("arm_near_upper", 55f), ("arm_near_fore", 30f), ("arm_far_upper", 45f), ("arm_far_fore", 30f),
            ("hair_back_1", -6f), ("hair_back_2", -4f), ("coat_back_1", -6f), ("coat_front_1", 4f), ("skirt", 2f));
        var sFall = P(0f, ("leg_rear_upper", 10f), ("leg_rear_lower", -22f), ("foot_rear", -5f), ("leg_front_upper", 14f), ("leg_front_lower", -22f), ("foot_front", -5f),
            ("spine", 0f), ("neck", -1f), ("head", -3f),
            ("arm_near_upper", 35f), ("arm_near_fore", 15f), ("arm_far_upper", 25f), ("arm_far_fore", 15f),
            ("hair_back_1", -16f), ("hair_back_2", -12f), ("hair_back_3", -10f), ("hair_front_near_1", -10f), ("hair_front_far_1", -10f),
            ("coat_back_1", -18f), ("coat_back_2", -10f), ("coat_front_1", 16f), ("coat_front_2", 8f), ("skirt", 8f));

        // ---------- moving air set (game style: SLYNYRD run'n'gun + OpenGameArt platformer base) ----------
        // a stride carried into the air: lead (front) knee up, trail (rear) leg hanging down; arms spread when falling
        var mRise = P(0f, ("leg_front_upper", 65f), ("leg_front_lower", -75f), ("foot_front", -10f),
            ("leg_rear_upper", -12f), ("leg_rear_lower", -20f), ("foot_rear", -20f),
            ("neck", 3f), ("head", 6f),
            ("arm_far_upper", 45f), ("arm_far_fore", 40f), ("arm_near_upper", -35f), ("arm_near_fore", 20f),
            ("hair_back_1", -5f), ("hair_back_2", -3f), ("coat_back_1", -6f), ("coat_front_1", -4f), ("skirt", -2f));
        var mApex = P(0f, ("leg_front_upper", 60f), ("leg_front_lower", -80f), ("foot_front", -10f),
            ("leg_rear_upper", 20f), ("leg_rear_lower", -45f), ("foot_rear", -15f),
            ("neck", 2f), ("head", 3f),
            ("arm_far_upper", 65f), ("arm_far_fore", 25f), ("arm_near_upper", -45f), ("arm_near_fore", 15f),
            ("hair_back_1", -9f), ("hair_back_2", -6f), ("hair_back_3", -5f), ("coat_back_1", -10f), ("coat_front_1", 6f), ("skirt", 3f));
        var mFall = P(0f, ("leg_front_upper", 35f), ("leg_front_lower", -55f), ("foot_front", 5f),
            ("leg_rear_upper", 3f), ("leg_rear_lower", -8f), ("foot_rear", -10f),
            ("neck", -1f), ("head", -2f),
            ("arm_far_upper", 85f), ("arm_far_fore", 15f), ("arm_near_upper", -70f), ("arm_near_fore", 10f),
            ("hair_back_1", -18f), ("hair_back_2", -13f), ("hair_back_3", -11f), ("hair_front_near_1", -10f), ("hair_front_far_1", -10f),
            ("coat_back_1", -22f), ("coat_back_2", -11f), ("coat_front_1", 14f), ("coat_front_2", 8f), ("skirt", 7f));

        // whole-body lean from the references (half pelvis, half chest); ground poses keep their feet planted
        crouch = Flat(Lean(crouch, -22f, -23f, true));
        impact = Flat(Lean(impact, -18f, -22f, true));
        Lean(sRise, 3f, 4f, false); Lean(sApex, -8f, -8f, false); Lean(sFall, 2f, 0f, false);
        Lean(mRise, -7f, -8f, false); Lean(mApex, -4f, -4f, false); Lean(mFall, 0f, 0f, false);

        var push = Lean(P(0f, ("leg_rear_upper", -6f), ("leg_rear_lower", -6f), ("foot_rear", -25f), ("leg_front_upper", -2f), ("leg_front_lower", -6f), ("foot_front", -25f),
            ("neck", 1f), ("head", 3f), ("arm_near_upper", 80f), ("arm_near_fore", 15f), ("arm_far_upper", 70f), ("arm_far_fore", 15f),
            ("hair_back_1", 4f), ("coat_back_1", 4f), ("skirt", -1f)), 0f, -4f, false);

        // air poses are short loops: the pose held, with hair/coat fluttering
        var flutter = new Dictionary<string, Wave>
        {
            ["hair_back_2"] = p => Flutter(p, 2f, 0.25f), ["hair_back_3"] = p => Flutter(p, 4f, 0.4f),
            ["coat_back_2"] = p => Flutter(p, 3f, 0.35f), ["coat_front_2"] = p => Flutter(p, 3f, 0.5f), ["skirt"] = p => Flutter(p, 1.5f, 0.15f),
        };
        AnimationClip Hold(string name, Pose pose) => MakeClip(name, animRoot, PoseTracks(new[] { (0f, pose), (1f, pose) }, flutter), 0.5f, 10);

        var clips = new Dictionary<string, AnimationClip>
        {
            // 2 frames (0.066 s): the crouch overlaps the start of the rise, the jump itself fires instantly
            // ends on Toon Boom frame 4 "push" (body straight, arms swinging forward-up): fits standing and running jumps
            ["JumpStart"] = MakeClip("Player_JumpStart_Draft", animRoot, PoseTracks(new[] { (0f, crouch), (1f, push) }, solveBob: true, animRoot: animRoot), 0.066f, 2, loop: false),
            // frames 0-2 compression, then back to idle (0.18 s)
            ["Land"] = MakeClip("Player_Land_Draft", animRoot, PoseTracks(new[] { (0f, impact), (0.35f, impact), (1f, idle0) }, solveBob: true, animRoot: animRoot), 0.18f, 6, loop: false),
            ["StandRise"] = Hold("Player_Air_StandRise_Draft", sRise),
            ["StandApex"] = Hold("Player_Air_StandApex_Draft", sApex),
            ["StandFall"] = Hold("Player_Air_StandFall_Draft", sFall),
            ["MoveRise"] = Hold("Player_Air_MoveRise_Draft", mRise),
            ["MoveApex"] = Hold("Player_Air_MoveApex_Draft", mApex),
            ["MoveFall"] = Hold("Player_Air_MoveFall_Draft", mFall),
        };
        return clips;
    }

    const string AirStateName = "Air";

    // Jump part of the controller. Parameters (set them from the player script every frame):
    //   Jump (trigger)  - fire when the jump starts
    //   Grounded (bool) - feet on the ground
    //   VelY (float)    - vertical speed in units/s (negative = falling)
    //   AirVel (float)  - VelY / jump take-off speed, clamped to -1..1 (+1 rise, 0 apex, -1 fall)
    //   Speed (float)   - horizontal speed (already used by Walk/Run); 0 = standing jump poses, >= 3 = moving jump poses
    // States: JumpStart -> Air (2D blend tree: Speed x AirVel) -> Land -> Idle/Walk/Run. Walking off a ledge goes
    // straight to Air. Old JumpRise/JumpFall states from the first draft are replaced.
    static void EnsureJumpStates(AnimatorController ctrl, Dictionary<string, AnimationClip> clips)
    {
        void Param(string n, AnimatorControllerParameterType t)
        {
            if (ctrl.parameters.All(p => p.name != n)) ctrl.AddParameter(n, t);
        }
        Param("Jump", AnimatorControllerParameterType.Trigger);
        Param("Grounded", AnimatorControllerParameterType.Bool);
        Param("VelY", AnimatorControllerParameterType.Float);
        Param("AirVel", AnimatorControllerParameterType.Float);
        var ps = ctrl.parameters; foreach (var p in ps) if (p.name == "Grounded") p.defaultBool = true; ctrl.parameters = ps;

        var sm = ctrl.layers[0].stateMachine;
        AnimatorState Get(string n) => sm.states.Select(s => s.state).FirstOrDefault(s => s.name == n);
        var airState = Get(AirStateName);
        if (airState != null)
        {
            // already built: just make sure the motions point at the (regenerated) clips
            Get("JumpStart").motion = clips["JumpStart"]; Get("Land").motion = clips["Land"];
            return;
        }
        // remove the first-draft jump states and the any-state transitions that led to them
        foreach (var n in new[] { "JumpStart", "JumpRise", "JumpFall", "Land" })
        {
            var st = Get(n);
            if (st == null) continue;
            foreach (var t in sm.anyStateTransitions.Where(t => t.destinationState == st).ToArray()) sm.RemoveAnyStateTransition(t);
            foreach (var s in sm.states.Select(x => x.state))
                foreach (var t in s.transitions.Where(t => t.destinationState == st).ToArray()) s.RemoveTransition(t);
            sm.RemoveState(st);
        }
        foreach (var n in new[] { "Player_JumpRise_Draft", "Player_JumpFall_Draft" })
            AssetDatabase.DeleteAsset($"{ClipDir}/{n}.anim");

        var idle = Get("Idle"); var walk = Get("Walk"); var run = Get("Run");
        var sStart = sm.AddState("JumpStart"); sStart.motion = clips["JumpStart"];
        var sLand = sm.AddState("Land"); sLand.motion = clips["Land"];

        var tree = new BlendTree { name = "AirBlend", blendType = BlendTreeType.FreeformCartesian2D, blendParameter = "Speed", blendParameterY = "AirVel", hideFlags = HideFlags.HideInHierarchy };
        AssetDatabase.AddObjectToAsset(tree, ctrl);
        tree.AddChild(clips["StandRise"], new Vector2(0f, 1f));
        tree.AddChild(clips["StandApex"], new Vector2(0f, 0f));
        tree.AddChild(clips["StandFall"], new Vector2(0f, -1f));
        tree.AddChild(clips["MoveRise"], new Vector2(3f, 1f));
        tree.AddChild(clips["MoveApex"], new Vector2(3f, 0f));
        tree.AddChild(clips["MoveFall"], new Vector2(3f, -1f));
        var sAir = sm.AddState(AirStateName); sAir.motion = tree;

        var any = sm.AddAnyStateTransition(sStart); any.hasExitTime = false; any.duration = 0.02f; any.canTransitionToSelf = false;
        any.AddCondition(AnimatorConditionMode.If, 0, "Jump");
        var offLedge = sm.AddAnyStateTransition(sAir); offLedge.hasExitTime = false; offLedge.duration = 0.1f; offLedge.canTransitionToSelf = false;
        offLedge.AddCondition(AnimatorConditionMode.IfNot, 0, "Grounded"); offLedge.AddCondition(AnimatorConditionMode.Less, -0.5f, "VelY");
        var t1 = sStart.AddTransition(sAir); t1.hasExitTime = true; t1.exitTime = 1f; t1.duration = 0.04f;
        var t2 = sAir.AddTransition(sLand); t2.hasExitTime = false; t2.duration = 0.02f; t2.AddCondition(AnimatorConditionMode.If, 0, "Grounded");
        var t3 = sLand.AddTransition(idle); t3.hasExitTime = true; t3.exitTime = 1f; t3.duration = 0.06f;
        if (run != null) { var t = sLand.AddTransition(run); t.hasExitTime = true; t.exitTime = 0.35f; t.duration = 0.08f; t.AddCondition(AnimatorConditionMode.Greater, 3.5f, "Speed"); }
        if (walk != null) { var t = sLand.AddTransition(walk); t.hasExitTime = true; t.exitTime = 0.35f; t.duration = 0.08f; t.AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed"); }
        EditorUtility.SetDirty(ctrl);
        Debug.Log("[PlayerDraftClips] Jump: JumpStart -> Air (Speed x AirVel blend) -> Land in " + ControllerPath);
    }

    // ---------------- Dash & slide (draft) ----------------
    // Dash pose from the ZeggyGames platformer template DASH (https://zegley.itch.io/2d-platformermetroidvania-asset-pack):
    // body ~40 deg forward, lead leg bent forward-down, trail leg straight back, both arms swept back, hair/coat streaming.
    // Slide: the rig drops into a reclined sit (in/out clips); while sliding the hand-drawn slide sprite is shown
    // (Assets/Character/Slide, from ArtSource/NAI_V5_rig_v3/slide_frames/slide_dash_reclined_loop_*).
    static Dictionary<string, AnimationClip> MakeDashSlideClips(Transform animRoot)
    {
        var idle0 = Flat(P(0f, ("spine", -4f), ("neck", 2f), ("head", 2f), ("arm_near_upper", 4f), ("arm_far_upper", 4f),
            ("leg_rear_upper", 0f), ("leg_rear_lower", 0f), ("leg_front_upper", 0f), ("leg_front_lower", 0f)));

        // References: ZeggyGames DASH, deviltearbasil female sidescroller DASH, anime dash pose notes.
        // Whole body leans ~38 deg forward with the head going with it; trail (rear) leg straight back in line with the
        // body; lead (front) knee bent with the foot under the hips; arms low: near arm swept back along the body,
        // far arm bent forward (opposite to the legs). The first draft raised both arms high behind and kicked the
        // rear leg up horizontally, which read as falling backwards.
        var dash = Lean(P(0f, ("leg_front_upper", 88f), ("leg_front_lower", -128f), ("foot_front", 5f),
            ("leg_rear_upper", -22f), ("leg_rear_lower", -6f), ("foot_rear", -25f),
            // head lifted so she looks ahead along the dash (body is ~55 deg forward)
            ("neck", 12f), ("head", 26f),
            ("arm_near_upper", -40f), ("arm_near_fore", 25f), ("arm_far_upper", 35f), ("arm_far_fore", 75f),
            ("hair_back_1", -20f), ("hair_back_2", -14f), ("hair_back_3", -12f), ("hair_front_near_1", -12f), ("hair_front_far_1", -12f),
            // coat and skirt stay together (both hang from the pelvis): a little trailing only, no opposite swings
            ("coat_back_1", -12f), ("coat_back_2", -10f), ("coat_front_1", -4f), ("coat_front_2", -4f), ("skirt", -2f)), -30f, -25f, false);
        var recover = Lean(P(0f, ("leg_front_upper", 15f), ("leg_front_lower", -25f), ("leg_rear_upper", -20f), ("leg_rear_lower", -15f),
            ("neck", 4f), ("head", 8f),
            ("arm_near_upper", -30f), ("arm_near_fore", 15f), ("arm_far_upper", 20f), ("arm_far_fore", 20f),
            ("hair_back_1", -10f), ("hair_back_2", -6f), ("coat_back_1", -12f), ("coat_front_1", -5f), ("skirt", -3f)), -8f, -8f, false);
        // slide entry/exit pose matches the slide art (slide_kneeup_4frames.webp): reclined sit, near leg stretched
        // forward, far knee up, far hand on the cap, near hand on the floor behind
        // pelvis tips back 45 deg (it also carries the legs): near leg ends ~horizontal, far knee points up
        var slideLow = Lean(P(0f, ("leg_front_upper", 55f), ("leg_front_lower", -8f), ("leg_rear_upper", 95f), ("leg_rear_lower", -120f),
            ("neck", -6f), ("head", -8f),
            ("arm_far_upper", 150f), ("arm_far_fore", 95f), ("arm_near_upper", -55f), ("arm_near_fore", 5f),
            ("hair_back_1", -18f), ("hair_back_2", -12f), ("coat_back_1", -20f), ("coat_front_1", 10f), ("skirt", 6f)), 45f, 15f, false);

        var flutter = new Dictionary<string, Wave>
        {
            ["hair_back_2"] = p => Flutter(p, 3f, 0.2f, 2f), ["hair_back_3"] = p => Flutter(p, 5f, 0.35f, 2f),
            ["coat_back_2"] = p => Flutter(p, 4f, 0.3f, 2f), ["coat_front_2"] = p => Flutter(p, 3f, 0.5f, 2f),
        };
        return new Dictionary<string, AnimationClip>
        {
            ["Dash"] = MakeClip("Player_Dash_Draft", animRoot, PoseTracks(new[] { (0f, dash), (1f, dash) }, flutter, solveBob: true, animRoot: animRoot), 0.3f, 9),
            ["DashEnd"] = MakeClip("Player_DashEnd_Draft", animRoot, PoseTracks(new[] { (0f, dash), (0.4f, recover), (1f, idle0) }, solveBob: true, animRoot: animRoot), 0.18f, 6, loop: false),
            ["SlideIn"] = MakeClip("Player_SlideIn_Draft", animRoot, PoseTracks(new[] { (0f, idle0), (1f, slideLow) }, solveBob: true, animRoot: animRoot), 0.08f, 3, loop: false),
            ["SlideOut"] = MakeClip("Player_SlideOut_Draft", animRoot, PoseTracks(new[] { (0f, slideLow), (1f, idle0) }, solveBob: true, animRoot: animRoot), 0.16f, 5, loop: false),
        };
    }

    // Parameters: Dashing (bool), Sliding (bool). States: Dash -> DashEnd -> Idle/Run/Air, SlideIn (holds) -> SlideOut -> Idle/Run.
    static void EnsureDashSlideStates(AnimatorController ctrl, Dictionary<string, AnimationClip> clips)
    {
        void Param(string n, AnimatorControllerParameterType t) { if (ctrl.parameters.All(p => p.name != n)) ctrl.AddParameter(n, t); }
        Param("Dashing", AnimatorControllerParameterType.Bool);
        Param("Sliding", AnimatorControllerParameterType.Bool);
        var sm = ctrl.layers[0].stateMachine;
        AnimatorState Get(string n) => sm.states.Select(s => s.state).FirstOrDefault(s => s.name == n);
        if (Get("Dash") != null)
        {
            Get("Dash").motion = clips["Dash"]; Get("DashEnd").motion = clips["DashEnd"];
            Get("SlideIn").motion = clips["SlideIn"]; Get("SlideOut").motion = clips["SlideOut"];
            return;
        }
        var idle = Get("Idle"); var run = Get("Run"); var air = Get(AirStateName);
        var sDash = sm.AddState("Dash"); sDash.motion = clips["Dash"];
        var sDashEnd = sm.AddState("DashEnd"); sDashEnd.motion = clips["DashEnd"];
        var sSlideIn = sm.AddState("SlideIn"); sSlideIn.motion = clips["SlideIn"];
        var sSlideOut = sm.AddState("SlideOut"); sSlideOut.motion = clips["SlideOut"];

        var a1 = sm.AddAnyStateTransition(sDash); a1.hasExitTime = false; a1.duration = 0.02f; a1.canTransitionToSelf = false;
        a1.AddCondition(AnimatorConditionMode.If, 0, "Dashing");
        var a2 = sm.AddAnyStateTransition(sSlideIn); a2.hasExitTime = false; a2.duration = 0.02f; a2.canTransitionToSelf = false;
        a2.AddCondition(AnimatorConditionMode.If, 0, "Sliding");
        // keep the any-state "walked off a ledge -> Air" from cutting a dash or slide short
        foreach (var t in sm.anyStateTransitions.Where(t => t.destinationState == air))
            foreach (var n in new[] { "Dashing", "Sliding" })
                if (t.conditions.All(c => c.parameter != n)) t.AddCondition(AnimatorConditionMode.IfNot, 0, n);

        var d1 = sDash.AddTransition(sDashEnd); d1.hasExitTime = false; d1.duration = 0.02f; d1.AddCondition(AnimatorConditionMode.IfNot, 0, "Dashing");
        var d2 = sDashEnd.AddTransition(air); d2.hasExitTime = false; d2.duration = 0.06f; d2.AddCondition(AnimatorConditionMode.IfNot, 0, "Grounded");
        var d3 = sDashEnd.AddTransition(run); d3.hasExitTime = true; d3.exitTime = 0.4f; d3.duration = 0.08f; d3.AddCondition(AnimatorConditionMode.Greater, 3.5f, "Speed");
        var d4 = sDashEnd.AddTransition(idle); d4.hasExitTime = true; d4.exitTime = 1f; d4.duration = 0.06f;
        var s1 = sSlideIn.AddTransition(sSlideOut); s1.hasExitTime = false; s1.duration = 0.02f; s1.AddCondition(AnimatorConditionMode.IfNot, 0, "Sliding");
        var s2 = sSlideOut.AddTransition(run); s2.hasExitTime = true; s2.exitTime = 0.5f; s2.duration = 0.08f; s2.AddCondition(AnimatorConditionMode.Greater, 3.5f, "Speed");
        var s3 = sSlideOut.AddTransition(idle); s3.hasExitTime = true; s3.exitTime = 1f; s3.duration = 0.06f;
        EditorUtility.SetDirty(ctrl);
        Debug.Log("[PlayerDraftClips] Added Dash/DashEnd/SlideIn/SlideOut to " + ControllerPath);
    }

    // ---------------- Crouch (draft) ----------------
    // References (ArtSource/references/crouch_reference.md):
    //  - crouch: Dimensions.com "Crouching - Women (Side)", figure 2 - front foot flat with the knee up at hip height,
    //    rear knee low with the heel lifted, trunk folded forward over the thighs, head up looking ahead.
    //  - crouch walk: Animation Mentor sneak walk - contact / down / passing / up, short steps, hips rise a little at
    //    passing, upper body kept from the contact pose (no spine drift); game packs keep crouch idle + crouch walk
    //    as their own states and keep the crouch baseline the same as the other states.
    // Legs are solved with 2-bone IK from foot targets, so the planted foot really stays put on the floor.
    const float CrouchJoint = 0.88f;     // leg joint height while crouched (standing: 1.49)
    const float CrouchWalkJoint = 0.94f;  // a little higher while moving (sneak: hips rise at passing)
    const float CrouchStride = 0.6f;     // foot travel per step (units)
    public const float CrouchWalkCycle = 0.86f;  // s per 2 steps; each foot is planted for half of it
    public const float CrouchWalkSpeed = CrouchStride / (0.5f * CrouchWalkCycle);   // stance = half the cycle

    static Dictionary<string, Transform> s_Bones;
    static Transform B(Transform animRoot, string n)
    {
        if (s_Bones == null || !s_Bones.ContainsKey(n) || s_Bones[n] == null)
            s_Bones = animRoot.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
        return s_Bones[n];
    }
    static float Ang(Vector2 v) => Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;
    static Vector2 Rot(Vector2 v, float deg) { float r = deg * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r); return new Vector2(c * v.x - s * v.y, s * v.x + c * v.y); }

    // Leg angles (offsets from bind, as used in Pose) that put the ankle on `ankle` (model space, bind = feet on the
    // floor at y = 0) with the knee bent forward, given the pelvis rotation and the hip's vertical offset.
    static void LegIK(Transform animRoot, Pose p, string side, Vector2 ankle)
    {
        Vector2 hip = B(animRoot, "hip").position, up = B(animRoot, "leg_" + side + "_upper").position;
        Vector2 lo = B(animRoot, "leg_" + side + "_lower").position, ft = B(animRoot, "foot_" + side).position;
        float hipRot = p.TryGetValue("hip", out var h) ? h : 0f;
        Vector2 j = hip + new Vector2(0, p.bob) + Rot(up - hip, hipRot);
        float l1 = (lo - up).magnitude, l2 = (ft - lo).magnitude;
        Vector2 v = ankle - j;
        float d = Mathf.Clamp(v.magnitude, Mathf.Abs(l1 - l2) + 1e-3f, l1 + l2 - 1e-3f);
        float alpha = Mathf.Acos(Mathf.Clamp((l1 * l1 + d * d - l2 * l2) / (2 * l1 * d), -1f, 1f)) * Mathf.Rad2Deg;
        float thighAbs = Ang(v) + alpha;                                   // knee forward (+X)
        Vector2 knee = j + Rot(Vector2.right, thighAbs) * l1;
        float shinAbs = Ang(ankle - knee);
        float thigh = Mathf.DeltaAngle(0f, thighAbs - Ang(lo - up) - hipRot);
        float kneeD = Mathf.DeltaAngle(0f, shinAbs - Ang(ft - lo) - hipRot - thigh);
        p["leg_" + side + "_upper"] = thigh; p["leg_" + side + "_lower"] = kneeD;
    }

    static Dictionary<string, AnimationClip> MakeCrouchClips(Transform animRoot)
    {
        s_Bones = null;
        float jointY = B(animRoot, "leg_front_upper").position.y;   // 1.49
        Vector2 Joint(string side) => B(animRoot, "leg_" + side + "_upper").position;
        Vector2 Ankle(string side) => B(animRoot, "foot_" + side).position;  // bind ankle: its height is the "on the floor" level

        // upper body shared by both clips: trunk folded ~50 deg (pelvis 22 + chest 28), head up to look ahead
        Pose Upper(float bob)
        {
            var p = P(bob, ("neck", 16f), ("head", 26f),
                ("arm_near_upper", 28f), ("arm_near_fore", 45f), ("arm_far_upper", 20f), ("arm_far_fore", 55f),
                // long back hair would reach the floor: lift it backward a little
                ("hair_back_1", -8f), ("hair_back_2", -6f), ("hair_back_3", -4f),
                // the coat hangs from the tilted pelvis: bring it back toward vertical and fold it a little so the
                // tail lies behind her instead of pushing into the floor
                ("coat_back_1", 14f), ("coat_back_2", -18f), ("coat_front_1", 18f), ("coat_front_2", 34f), ("skirt", 4f));   // front flap: lower panel folds up so its tip stays off the floor
            p["hip"] = -22f; p["spine"] = -28f;
            return p;
        }

        // ---- crouch idle ----
        Pose CrouchPose(float breathe)
        {
            var p = Upper(CrouchJoint - jointY + breathe);
            // front foot flat ~0.28 ahead of its hip joint; rear foot ~0.3 behind, heel lifted onto the toes
            LegIK(animRoot, p, "front", new Vector2(Joint("front").x + 0.32f, Ankle("front").y));
            LegIK(animRoot, p, "rear", new Vector2(Joint("rear").x - 0.30f, Ankle("rear").y + 0.07f));
            Flat(p);
            p["foot_rear"] -= 38f; p["toe_rear"] = 30f;   // heel up, toe cap flat on the floor
            p["spine"] += 1.2f * breathe / 0.008f * 0.5f;
            return p;
        }
        var idleTracks = new List<Track>();
        {
            var a = CrouchPose(0f); var b = CrouchPose(-0.008f);
            foreach (var bone in a.Keys.ToList())
            {
                string n = bone; float va = a[n], vb = b[n];
                idleTracks.Add(new Track { bone = n, rot = q => Mathf.Lerp(va, vb, 0.5f - 0.5f * Mathf.Cos(q * Mathf.PI * 2f)) });
            }
            idleTracks.Add(new Track { bone = "hip", bobY = q => Mathf.Lerp(a.bob, b.bob, 0.5f - 0.5f * Mathf.Cos(q * Mathf.PI * 2f)) });
            foreach (var t in idleTracks.Where(t => t.bone == "hair_back_3" || t.bone == "coat_back_2").ToList())
            { var r = t.rot; t.rot = q => r(q) + 1.2f * S(q, 0.3f); }
        }

        // ---- crouch walk ----
        // foot path per leg, phase 0 = contact (foot forward): stance 0..0.5 the foot slides back under her on the
        // floor (flat), push-off at the end lifts the heel; swing 0.5..1 it lifts ~0.09 and comes forward.
        Vector2 FootAt(string side, float q, out float heel, out float tilt)
        {
            float half = CrouchStride * 0.5f, x, y = 0f; heel = 0f; tilt = 0f;
            if (q < 0.5f)
            {
                float u = q / 0.5f;
                x = Mathf.Lerp(half, -half, u);
                heel = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.7f, 1f, u));   // push-off: heel comes up
                y = 0.05f * heel;
            }
            else
            {
                float u = (q - 0.5f) / 0.5f;
                x = Mathf.Lerp(-half, half, Mathf.SmoothStep(0f, 1f, u));
                y = 0.09f * Mathf.Sin(u * Mathf.PI) + 0.05f * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.3f, u)));
                tilt = 18f * Mathf.Sin(u * Mathf.PI);                                // toes hang a little in the air
                heel = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.25f, u));
            }
            // centre each foot slightly ahead of its hip joint (crouched weight sits over the front of the feet)
            return new Vector2(Joint(side).x + 0.02f + x, Ankle(side).y + y);
        }
        Pose WalkPose(float q)
        {
            // contact (q = 0, 0.5) lowest, passing (0.25, 0.75) highest
            float bob = CrouchWalkJoint - jointY - 0.025f * Mathf.Cos(q * Mathf.PI * 4f);
            var p = Upper(bob);
            float hr, tr, hf, tf;
            var fr = FootAt("rear", q, out hr, out tr);
            var ff = FootAt("front", (q + 0.5f) % 1f, out hf, out tf);
            LegIK(animRoot, p, "rear", fr); LegIK(animRoot, p, "front", ff);
            Flat(p);
            p["foot_rear"] -= 30f * hr + tr; p["toe_rear"] = 26f * hr;
            p["foot_front"] -= 30f * hf + tf; p["toe_front"] = 26f * hf;
            // arms: small swing against the legs (near arm with the rear leg), chest stays as in contact
            float sw = Mathf.Sin(q * Mathf.PI * 2f);
            p["arm_near_upper"] += 10f * sw; p["arm_far_upper"] -= 10f * sw;
            p["spine"] += 1f * Mathf.Cos(q * Mathf.PI * 4f);
            p["hair_back_2"] += 2f * Mathf.Sin((q * 2f + 0.3f) * Mathf.PI * 2f);
            p["hair_back_3"] += 3f * Mathf.Sin((q * 2f + 0.45f) * Mathf.PI * 2f);
            p["coat_back_2"] += 3f * Mathf.Sin((q * 2f + 0.35f) * Mathf.PI * 2f);
            p["skirt"] += 1.5f * Mathf.Sin((q * 2f + 0.2f) * Mathf.PI * 2f);
            return p;
        }
        var walkTracks = new List<Track>();
        var cache = new Dictionary<float, Pose>();
        Pose W(float q) { if (!cache.TryGetValue(q, out var p)) cache[q] = p = WalkPose(q); return p; }
        foreach (var bone in W(0f).Keys.ToList())
        {
            string n = bone;
            walkTracks.Add(new Track { bone = n, rot = q => W(q).TryGetValue(n, out var v) ? v : 0f });
        }
        walkTracks.Add(new Track { bone = "hip", bobY = q => W(q).bob });

        return new Dictionary<string, AnimationClip>
        {
            ["Crouch"] = MakeClip("Player_Crouch_Draft", animRoot, idleTracks, 2.0f, 8),
            ["CrouchWalk"] = MakeClip("Player_CrouchWalk_Draft", animRoot, walkTracks, CrouchWalkCycle, 24),
        };
    }

    // Parameter: Crouching (bool). State "Crouch" = 1D blend on Speed (0: Crouch, CrouchWalkSpeed: CrouchWalk).
    // Any state -> Crouch while Crouching (grounded, not dashing/sliding); Crouch -> Idle/Run when she stands up.
    static void EnsureCrouchStates(AnimatorController ctrl, Dictionary<string, AnimationClip> clips)
    {
        if (ctrl.parameters.All(p => p.name != "Crouching")) ctrl.AddParameter("Crouching", AnimatorControllerParameterType.Bool);
        var sm = ctrl.layers[0].stateMachine;
        AnimatorState Get(string n) => sm.states.Select(s => s.state).FirstOrDefault(s => s.name == n);
        var existing = Get("Crouch");
        if (existing != null)
        {
            var bt = (BlendTree)existing.motion;
            var ch = bt.children; ch[0].motion = clips["Crouch"]; ch[1].motion = clips["CrouchWalk"]; ch[1].threshold = CrouchWalkSpeed; bt.children = ch;
            EditorUtility.SetDirty(ctrl);
            return;
        }
        var tree = new BlendTree { name = "CrouchBlend", blendType = BlendTreeType.Simple1D, blendParameter = "Speed", useAutomaticThresholds = false, hideFlags = HideFlags.HideInHierarchy };
        AssetDatabase.AddObjectToAsset(tree, ctrl);
        tree.AddChild(clips["Crouch"], 0f);
        tree.AddChild(clips["CrouchWalk"], CrouchWalkSpeed);
        var sCrouch = sm.AddState("Crouch"); sCrouch.motion = tree;

        var a = sm.AddAnyStateTransition(sCrouch); a.hasExitTime = false; a.duration = 0.1f; a.canTransitionToSelf = false;
        a.AddCondition(AnimatorConditionMode.If, 0, "Crouching");
        a.AddCondition(AnimatorConditionMode.If, 0, "Grounded");
        a.AddCondition(AnimatorConditionMode.IfNot, 0, "Sliding");
        a.AddCondition(AnimatorConditionMode.IfNot, 0, "Dashing");
        var toRun = sCrouch.AddTransition(Get("Run")); toRun.hasExitTime = false; toRun.duration = 0.12f;
        toRun.AddCondition(AnimatorConditionMode.IfNot, 0, "Crouching"); toRun.AddCondition(AnimatorConditionMode.Greater, 3.5f, "Speed");
        var toWalk = sCrouch.AddTransition(Get("Walk")); toWalk.hasExitTime = false; toWalk.duration = 0.12f;
        toWalk.AddCondition(AnimatorConditionMode.IfNot, 0, "Crouching"); toWalk.AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed");
        var toIdle = sCrouch.AddTransition(Get("Idle")); toIdle.hasExitTime = false; toIdle.duration = 0.12f;
        toIdle.AddCondition(AnimatorConditionMode.IfNot, 0, "Crouching");
        EditorUtility.SetDirty(ctrl);
        Debug.Log("[PlayerDraftClips] Added Crouch (Crouch/CrouchWalk blend) to " + ControllerPath);
    }

    // ---------------- Backpedal (draft) ----------------
    // Moving away from the aim (mouse) while facing it. References (ArtSource/references/backpedal_reference.md):
    //  - biomechanics of backward walking: joint patterns are close to a time-reversed forward walk, the TOES touch
    //    down first, stride and speed are shorter, cadence higher, stance takes a bigger share of the cycle and the
    //    knee bends less while loaded.
    //  - football defensive-back backpedal: low stance with the chest forward over the knees, weight on the balls of
    //    the feet (never back on the heels), short quick steps with the feet kept low, arms bent ~90 deg, never full speed.
    // Feet are placed with the same 2-bone IK as the crouch walk, so planted feet do not slide at BackpedalSpeed.
    const float BackJoint = 1.38f;                   // leg joint height (standing 1.49): knees a little bent
    const float BackStride = 0.6f;                   // foot travel per stance
    public const float BackpedalCycle = 0.46f;       // 2 steps; quick, short steps
    public const float BackpedalSpeed = BackStride / (0.5f * BackpedalCycle);   // = 2.6 u/s (PlayerController2D.backpedalSpeed)

    static AnimationClip MakeBackpedalClip(Transform animRoot)
    {
        s_Bones = null;
        float jointY = B(animRoot, "leg_front_upper").position.y;
        Vector2 Joint(string side) => B(animRoot, "leg_" + side + "_upper").position;
        Vector2 Ankle(string side) => B(animRoot, "foot_" + side).position;

        // per leg, phase 0 = toe touch-down BEHIND the hips. The body moves back (-X), so in stance (0..0.5) the planted
        // foot travels forward under her; at the end of stance the toes lift first (reverse of a heel-off).
        // Swing (0.5..1): the foot is carried back low and reaches back toes-down for the next contact.
        Vector2 FootAt(string side, float q, out float footRot)
        {
            float half = BackStride * 0.5f, x, y;
            if (q < 0.5f)
            {
                float u = q / 0.5f;
                x = Mathf.Lerp(-half, half, u);
                float load = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.3f, u));    // heel lowers after the toe touch
                float lift = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.75f, 1f, u));   // toes come up before the foot leaves
                footRot = -24f * (1f - load) + 14f * lift;
                y = 0.06f * (1f - load) + 0.02f * lift;
            }
            else
            {
                float u = (q - 0.5f) / 0.5f, e = Mathf.SmoothStep(0f, 1f, u);
                x = Mathf.Lerp(half, -half, e);
                footRot = Mathf.Lerp(14f, -24f, e);
                y = 0.08f * Mathf.Sin(u * Mathf.PI) + Mathf.Lerp(0.02f, 0.06f, u);       // feet kept low
            }
            return new Vector2(Joint(side).x - 0.03f + x, Ankle(side).y + y);
        }

        Pose At(float q)
        {
            // lowest while loading just after each toe contact, highest at the hand-over (both feet light)
            float bob = BackJoint - jointY - 0.02f * Mathf.Cos((q - 0.1f) * Mathf.PI * 4f);
            float sw = Mathf.Sin(q * Mathf.PI * 2f), sw2 = Mathf.Sin(q * Mathf.PI * 4f);
            var p = P(bob, ("neck", 5f), ("head", 9f),
                // arms bent ~90, short quick pumps opposite to the legs (the near arm is re-posed by the weapon aim)
                ("arm_near_upper", 12f - 14f * sw), ("arm_near_fore", 80f), ("arm_far_upper", 12f + 14f * sw), ("arm_far_fore", 85f),
                // moving backwards: hair and coat tails trail FORWARD (+ = tip toward +X)
                ("hair_back_1", 6f + 1.5f * sw2), ("hair_back_2", 4f + 2.5f * Mathf.Sin((q * 2f + 0.3f) * Mathf.PI * 2f)),
                ("hair_back_3", 3f + 3.5f * Mathf.Sin((q * 2f + 0.45f) * Mathf.PI * 2f)),
                ("hair_front_near_1", 3f), ("hair_front_far_1", 3f),
                ("coat_back_1", 8f + 2f * sw2), ("coat_back_2", 4f + 3f * Mathf.Sin((q * 2f + 0.35f) * Mathf.PI * 2f)),
                ("coat_front_1", 7f + 2f * sw2), ("coat_front_2", 4f + 3f * Mathf.Sin((q * 2f + 0.5f) * Mathf.PI * 2f)),
                ("skirt", 3f + 1.5f * Mathf.Sin((q * 2f + 0.2f) * Mathf.PI * 2f)));
            // chest forward over the knees: pelvis 9 + chest 9 deg
            p["hip"] = -9f; p["spine"] = -9f + 1f * sw2;
            float rr, rf;
            var fr = FootAt("rear", q, out rr);
            var ff = FootAt("front", (q + 0.5f) % 1f, out rf);
            LegIK(animRoot, p, "rear", fr); LegIK(animRoot, p, "front", ff);
            Flat(p);
            p["foot_rear"] += rr; p["foot_front"] += rf;
            // toe caps bend up while the foot is planted toes-down (contact), flat otherwise
            p["toe_rear"] = Mathf.Max(0f, -rr) * 0.6f; p["toe_front"] = Mathf.Max(0f, -rf) * 0.6f;
            return p;
        }

        var cache = new Dictionary<float, Pose>();
        Pose W(float q) { if (!cache.TryGetValue(q, out var p)) cache[q] = p = At(q); return p; }
        var tracks = new List<Track>();
        foreach (var bone in W(0f).Keys.ToList())
        {
            string n = bone;
            tracks.Add(new Track { bone = n, rot = q => W(q).TryGetValue(n, out var v) ? v : 0f });
        }
        tracks.Add(new Track { bone = "hip", bobY = q => W(q).bob });
        return MakeClip("Player_Backpedal_Draft", animRoot, tracks, BackpedalCycle, 24);
    }

    // Parameters: Backpedal (bool), MoveRate (float, clip speed = |vx| / BackpedalSpeed), MoveDir (float, +1/-1: the
    // Crouch state's speed, so the crouch walk plays in reverse when she crouch-walks away from the aim).
    static void EnsureBackpedalState(AnimatorController ctrl, AnimationClip clip)
    {
        void Param(string n, AnimatorControllerParameterType t, float def)
        {
            if (ctrl.parameters.All(p => p.name != n)) ctrl.AddParameter(new AnimatorControllerParameter { name = n, type = t, defaultFloat = def });
        }
        Param("Backpedal", AnimatorControllerParameterType.Bool, 0f);
        Param("MoveRate", AnimatorControllerParameterType.Float, 1f);
        Param("MoveDir", AnimatorControllerParameterType.Float, 1f);
        var sm = ctrl.layers[0].stateMachine;
        AnimatorState Get(string n) => sm.states.Select(s => s.state).FirstOrDefault(s => s.name == n);
        var crouch = Get("Crouch");
        if (crouch != null) { crouch.speedParameterActive = true; crouch.speedParameter = "MoveDir"; }
        var existing = Get("Backpedal");
        if (existing != null) { existing.motion = clip; EditorUtility.SetDirty(ctrl); return; }

        var st = sm.AddState("Backpedal"); st.motion = clip; st.speedParameterActive = true; st.speedParameter = "MoveRate";
        var a = sm.AddAnyStateTransition(st); a.hasExitTime = false; a.duration = 0.1f; a.canTransitionToSelf = false;
        a.AddCondition(AnimatorConditionMode.If, 0, "Backpedal");
        a.AddCondition(AnimatorConditionMode.If, 0, "Grounded");
        a.AddCondition(AnimatorConditionMode.IfNot, 0, "Crouching");
        a.AddCondition(AnimatorConditionMode.IfNot, 0, "Sliding");
        a.AddCondition(AnimatorConditionMode.IfNot, 0, "Dashing");
        var toRun = st.AddTransition(Get("Run")); toRun.hasExitTime = false; toRun.duration = 0.1f;
        toRun.AddCondition(AnimatorConditionMode.IfNot, 0, "Backpedal"); toRun.AddCondition(AnimatorConditionMode.Greater, 3.5f, "Speed");
        var toWalk = st.AddTransition(Get("Walk")); toWalk.hasExitTime = false; toWalk.duration = 0.1f;
        toWalk.AddCondition(AnimatorConditionMode.IfNot, 0, "Backpedal"); toWalk.AddCondition(AnimatorConditionMode.Greater, 0.6f, "Speed");
        var toIdle = st.AddTransition(Get("Idle")); toIdle.hasExitTime = false; toIdle.duration = 0.12f;
        toIdle.AddCondition(AnimatorConditionMode.IfNot, 0, "Backpedal");
        EditorUtility.SetDirty(ctrl);
        Debug.Log("[PlayerDraftClips] Added Backpedal to " + ControllerPath);
    }

    static AnimationClip MakeClip(string name, Transform animRoot, List<Track> tracks, float duration, int samples, bool loop = true)
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
                Set(clip, bpath, "localEulerAnglesRaw.x", duration, samples, _ => e.x, loop);
                Set(clip, bpath, "localEulerAnglesRaw.y", duration, samples, _ => e.y, loop);
                Set(clip, bpath, "localEulerAnglesRaw.z", duration, samples, p => e.z + tr.rot(p), loop);
            }
            if (tr.bobY != null)
            {
                Vector3 lp = bone.localPosition;
                Vector3 Local(float dy) => lp + bone.parent.InverseTransformVector(new Vector3(0, dy, 0));
                Set(clip, bpath, "m_LocalPosition.x", duration, samples, p => Local(tr.bobY(p)).x, loop);
                Set(clip, bpath, "m_LocalPosition.y", duration, samples, p => Local(tr.bobY(p)).y, loop);
                Set(clip, bpath, "m_LocalPosition.z", duration, samples, _ => lp.z, loop);
            }
        }
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        if (existing == null) AssetDatabase.CreateAsset(clip, path); else EditorUtility.SetDirty(clip);
        Debug.Log("[PlayerDraftClips] Created " + path);
        return clip;
    }

    static void Set(AnimationClip clip, string path, string prop, float duration, int samples, Func<float, float> f, bool loop = true)
    {
        var keys = new Keyframe[samples + 1];
        for (int i = 0; i <= samples; ++i)
        {
            float p = i / (float)samples;
            keys[i] = new Keyframe(p * duration, f(i == samples && loop ? 0f : p)); // looping: last key == first key
        }
        var curve = new AnimationCurve(keys);
        for (int i = 0; i < keys.Length; ++i) curve.SmoothTangents(i, 0f);
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), prop), curve);
    }
}
