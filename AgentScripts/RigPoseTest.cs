using System.Linq; using UnityEditor; using UnityEditor.SceneManagement; using UnityEngine;
public static class RigPoseTest {
  static Transform Find(Transform r, string n) => r.GetComponentsInChildren<Transform>(true).First(t => t.name == n);
  static void Rot(Transform r, string n, float deg) { var t = Find(r, n); t.localRotation *= Quaternion.Euler(0, 0, deg); }
  public static string Run() {
    var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
    var cam = Camera.main; cam.orthographic = true; cam.orthographicSize = 1.8f; cam.transform.position = new Vector3(0, 1.5f, -10);
    cam.backgroundColor = new Color(0.85f, 0.87f, 0.9f); cam.clearFlags = CameraClearFlags.SolidColor;
    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Character/Player.prefab");
    var a = (GameObject)PrefabUtility.InstantiatePrefab(prefab); a.name = "Player_BindPose"; a.transform.position = new Vector3(-1.4f, 0, 0);
    var b = (GameObject)PrefabUtility.InstantiatePrefab(prefab); b.name = "Player_PoseTest"; b.transform.position = new Vector3(1.4f, 0, 0);
    var r = b.transform; b.GetComponentInChildren<Animator>().enabled = false; a.GetComponentInChildren<Animator>().enabled = false;
    Rot(r, "arm_near_upper", 35); Rot(r, "arm_near_fore", 45);
    Rot(r, "arm_far_upper", -30); Rot(r, "arm_far_fore", -20);
    Rot(r, "leg_front_upper", -30); Rot(r, "leg_front_lower", 35);
    Rot(r, "leg_rear_upper", 25); Rot(r, "leg_rear_lower", 15);
    Rot(r, "head", 25); Rot(r, "neck", 6); Rot(r, "hair_back_2", 12); Rot(r, "hair_back_3", 12);
    Rot(r, "coat_back_1", -14); Rot(r, "coat_front_1", 10); Rot(r, "spine", 5);
    EditorSceneManager.SaveScene(scene, "Assets/Scenes/RigTest.unity");
    return "ok";
  }
}
