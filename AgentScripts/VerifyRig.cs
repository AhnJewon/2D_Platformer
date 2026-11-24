using System.Linq; using System.Text; using UnityEditor; using UnityEngine; using UnityEngine.U2D; using UnityEngine.U2D.Animation;
public static class VerifyRig {
  public static string Run() {
    var sb = new StringBuilder();
    foreach (var o in AssetDatabase.LoadAllAssetsAtPath("Assets/Characters/Officer/Player.psb")) if (o is Sprite s)
      sb.Append(s.name).Append(" bones=").Append(s.GetBones().Length).Append(" verts=").Append(s.GetVertexCount())
        .Append(" weights=").Append(s.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.BlendWeight)).Append('\n');
    var go = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Characters/Officer/Player.prefab");
    sb.Append("prefab=").Append(go != null);
    if (go != null) {
      var skins = go.GetComponentsInChildren<SpriteSkin>(true);
      sb.Append(" skins=").Append(skins.Length)
        .Append(" bound=").Append(skins.Count(k => k.boneTransforms != null && k.boneTransforms.Length > 0 && k.boneTransforms.All(t => t != null)))
        .Append(" boneGOs=").Append(go.GetComponentsInChildren<Transform>(true).Count(t => t.name.StartsWith("arm_") || t.name=="hip" || t.name=="head"));
    }
    return sb.ToString();
  }
}
