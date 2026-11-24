using UnityEditor; using UnityEditor.SceneManagement; using UnityEngine;
public static class ClipTestScene {
  public static string Run() {
    var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
    var cam = Camera.main; cam.orthographic = true; cam.orthographicSize = 1.8f; cam.transform.position = new Vector3(0, 1.5f, -10);
    cam.backgroundColor = new Color(0.85f, 0.87f, 0.9f); cam.clearFlags = CameraClearFlags.SolidColor;
    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Characters/Officer/Player.prefab");
    string[] states = { "Idle", "Walk", "Run" };
    for (int i = 0; i < 3; ++i) {
      var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab); go.name = "Player_" + states[i];
      go.transform.position = new Vector3(-2.1f + 2.1f * i, 0, 0);
      go.AddComponent<AnimatorStatePreview>().stateName = states[i];
    }
    EditorSceneManager.SaveScene(scene, "Assets/Scenes/RigTest.unity");
    return "ok";
  }
}
