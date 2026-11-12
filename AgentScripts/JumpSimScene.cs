using UnityEditor; using UnityEditor.SceneManagement; using UnityEngine;
// Builds Assets/Scenes/JumpSim.unity: Player + ground line + camera with JumpSimRecorder.
public static class JumpSimScene {
  public static string Run() {
    var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
    var cam = Camera.main; cam.orthographic = true; cam.orthographicSize = 2.6f; cam.transform.position = new Vector3(0.05f, 2.35f, -10);
    cam.backgroundColor = new Color(0.85f, 0.87f, 0.9f); cam.clearFlags = CameraClearFlags.SolidColor;
    var player = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Character/Player.prefab"));
    var ground = new GameObject("Ground"); var lr = ground.AddComponent<LineRenderer>();
    lr.positionCount = 2; lr.SetPositions(new[] { new Vector3(-3, 0.08f, 0), new Vector3(3, 0.08f, 0) }); lr.widthMultiplier = 0.012f;
    lr.material = new Material(Shader.Find("Sprites/Default")); lr.startColor = lr.endColor = new Color(0.3f, 0.3f, 0.35f);
    var rec = cam.gameObject.AddComponent<JumpSimRecorder>(); rec.player = player.transform;
    EditorSceneManager.SaveScene(scene, "Assets/Scenes/JumpSim.unity");
    return "ok";
  }
}
