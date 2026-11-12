using UnityEditor; using UnityEditor.SceneManagement; using UnityEngine;
public static class PersistVisual {
  public static string Run() {
    var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
    var cam = Camera.main; cam.orthographic = true; cam.orthographicSize = 1.6f; cam.transform.position = new Vector3(0, 1.55f, -10);
    cam.backgroundColor = Color.white; cam.clearFlags = CameraClearFlags.SolidColor;
    var a = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Character/Player.psb")); a.transform.position = new Vector3(-1.1f, 0, 0);
    var b = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_RigTest/Test.psb")); b.transform.position = new Vector3(1.1f, 0, 0);
    EditorSceneManager.SaveScene(scene, "Assets/_RigTest/PersistTest.unity");
    return "ok";
  }
}
