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
