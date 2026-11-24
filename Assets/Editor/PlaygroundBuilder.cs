using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Builds Assets/Scenes/Playground.unity: a test level to move the Player around (ground, steps, platforms, a gap).
// Menu: Tools > Player Rig > Build Playground Scene (overwrites that scene only).
public static class PlaygroundBuilder
{
    const string ScenePath = "Assets/Scenes/Playground.unity";
    const string SquarePath = "Assets/Art/Placeholder/Square.png";

    [MenuItem("Tools/Player Rig/Build Playground Scene")]
    public static void Build()
    {
        var square = EnsureSquare();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        var cam = Camera.main;
        cam.orthographic = true; cam.orthographicSize = 4.2f; cam.transform.position = new Vector3(0, 2.4f, -10);
        cam.backgroundColor = new Color(0.78f, 0.84f, 0.9f); cam.clearFlags = CameraClearFlags.SolidColor;

        var level = new GameObject("Level");
        void Block(string name, float x, float y, float w, float h, Color c)
        {
            var go = new GameObject(name);
            go.transform.SetParent(level.transform);
            go.transform.position = new Vector3(x, y, 0);
            go.transform.localScale = new Vector3(w, h, 1);
            var sr = go.AddComponent<SpriteRenderer>(); sr.sprite = square; sr.color = c; sr.sortingOrder = -10;
            go.AddComponent<BoxCollider2D>();
        }
        var ground = new Color(0.35f, 0.38f, 0.45f); var plat = new Color(0.45f, 0.5f, 0.58f);
        // ground top is at y = 0 (the Player's feet sit at y = 0)
        Block("Ground_Left", -6f, -1f, 20f, 2f, ground);
        Block("Ground_Right", 13.5f, -1f, 14f, 2f, ground);      // 2.5-unit gap between x = 4 and x = 6.5
        Block("Step_Low", -9f, 0.3f, 3f, 0.6f, plat);            // short hop
        Block("Platform_1", 0f, 0.95f, 3f, 0.3f, plat);         // top 1.1 (jump height 1.4)
        Block("Platform_2", 9f, 1.05f, 3f, 0.3f, plat);        // top 1.2
        Block("Platform_High", 13f, 2.15f, 2.5f, 0.3f, plat);   // top 2.3, reachable from Platform_2
        Block("Low_Ceiling", 16f, 2.1f, 2.5f, 1.8f, ground);     // bottom at 1.2: too low to walk under, slide under it
        Block("Wall_Left", -16.5f, 3f, 1f, 10f, ground);
        Block("Wall_Right", 21f, 3f, 1f, 10f, ground);

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Characters/Officer/Player.prefab");
        var player = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        player.transform.position = new Vector3(-4f, 0f, 0f);
        var rb = player.AddComponent<Rigidbody2D>();
        var col = player.AddComponent<CapsuleCollider2D>();
        // body column only (hair/coat stick out but should not collide); feet at 0.08 above the root
        col.size = new Vector2(0.5f, 2.55f); col.offset = new Vector2(0.12f, 0.08f + 2.55f / 2f);
        var slide = player.AddComponent<SlideVisual>();
        slide.frames = Enumerable.Range(0, 4).Select(i => AssetDatabase.LoadAssetAtPath<Sprite>($"Assets/Characters/Officer/Slide/Slide_{i:00}.png")).Where(f => f != null).ToArray();
        var ctrl = player.AddComponent<PlayerController2D>();
        player.AddComponent<DashFX>();
        var follow = cam.gameObject.AddComponent<CameraFollow2D>(); follow.target = player.transform;

        EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log("[PlaygroundBuilder] Built " + ScenePath + " - A/D or arrows run (Ctrl walks), Space/W/Up jump, Shift dash, Down+Shift slide");
    }

    static Sprite EnsureSquare()
    {
        if (!File.Exists(SquarePath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SquarePath));
            var tex = new Texture2D(4, 4);
            var px = new Color[16]; for (int i = 0; i < 16; ++i) px[i] = Color.white;
            tex.SetPixels(px); tex.Apply();
            File.WriteAllBytes(SquarePath, tex.EncodeToPNG());
            AssetDatabase.ImportAsset(SquarePath);
            var ti = (TextureImporter)AssetImporter.GetAtPath(SquarePath);
            ti.textureType = TextureImporterType.Sprite; ti.spritePixelsPerUnit = 4; ti.filterMode = FilterMode.Point;
            ti.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(SquarePath);
    }
}
