using UnityEditor; using UnityEditor.SceneManagement; using UnityEngine;
// Builds Assets/Scenes/PoseSheet.unity: Idle / Walk / Run draft clips at fixed phases, side by side.
public static class PoseSheetScene {
  public static string Run() {
    var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
    var cam = Camera.main; cam.orthographic = true; cam.orthographicSize = 4.9f; cam.transform.position = new Vector3(4.4f, -0.55f, -10);
    cam.backgroundColor = new Color(0.85f, 0.87f, 0.9f); cam.clearFlags = CameraClearFlags.SolidColor;
    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Characters/Officer/Player.prefab");
    var rows = new (string clip, float[] t)[] {
      ("Player_Idle_Draft", new[]{0f, 0.25f, 0.5f, 0.75f}),
      ("Player_Walk_Draft", new[]{0f, 0.125f, 0.25f, 0.375f, 0.5f, 0.55f}),
      ("Player_Run_Draft",  new[]{0f, 0.15f, 0.3f, 0.5f, 0.65f, 0.8f}) };
    for (int r = 0; r < rows.Length; ++r) {
      var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Characters/Officer/Animations/Draft/" + rows[r].clip + ".anim");
      for (int i = 0; i < rows[r].t.Length; ++i) {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab); go.name = rows[r].clip + "_" + rows[r].t[i];
        go.transform.position = new Vector3(i * 1.75f, -r * 3.2f + 1.1f, 0);
        var s = go.AddComponent<ClipPoseSampler>(); s.clip = clip; s.normalizedTime = rows[r].t[i];
      }
    }
    EditorSceneManager.SaveScene(scene, "Assets/Scenes/PoseSheet.unity");
    return "ok";
  }
}
