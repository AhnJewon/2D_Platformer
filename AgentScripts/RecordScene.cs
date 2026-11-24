using System.Linq; using UnityEditor; using UnityEditor.SceneManagement; using UnityEngine;
// Builds Assets/Scenes/ClipRecording.unity: one Player + a camera with ClipRecorder for the three draft clips.
public static class RecordScene {
  public static string Run() {
    var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
    var cam = Camera.main; cam.orthographic = true; cam.orthographicSize = 1.75f; cam.transform.position = new Vector3(0.05f, 1.55f, -10);
    cam.backgroundColor = new Color(0.85f, 0.87f, 0.9f); cam.clearFlags = CameraClearFlags.SolidColor;
    var player = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Characters/Officer/Player.prefab"));
    // ground line so foot contact is visible
    var ground = new GameObject("Ground"); var lr = ground.AddComponent<LineRenderer>();
    lr.positionCount = 2; lr.SetPositions(new[] { new Vector3(-2, 0.08f, 0), new Vector3(2, 0.08f, 0) }); lr.widthMultiplier = 0.01f;
    lr.material = new Material(Shader.Find("Sprites/Default")); lr.startColor = lr.endColor = new Color(0.3f, 0.3f, 0.35f);
    var rec = cam.gameObject.AddComponent<ClipRecorder>(); rec.target = player;
    rec.clips = new[] { "Crouch", "CrouchWalk" }.Select(n => AssetDatabase.LoadAssetAtPath<AnimationClip>($"Assets/Characters/Officer/Animations/Draft/Player_{n}_Draft.anim")).ToArray();
    EditorSceneManager.SaveScene(scene, "Assets/Scenes/ClipRecording.unity");
    return "ok";
  }
}
