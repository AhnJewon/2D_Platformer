using System.Linq; using System.Text; using UnityEditor; using UnityEditor.U2D.Animation; using UnityEditor.U2D.PSD; using UnityEditor.U2D.Sprites; using UnityEngine; using UnityEngine.U2D;
public static class RigPersistTest {
  const string T = "Assets/_RigTest/Test.psb";
  static ISpriteEditorDataProvider DP(string p) { var f = new SpriteDataProviderFactories(); f.Init(); var d = f.GetSpriteEditorDataProviderFromObject(AssetImporter.GetAtPath(p)); d.InitSpriteEditorDataProvider(); return d; }
  public static string Copy() {
    AssetDatabase.DeleteAsset("Assets/_RigTest"); AssetDatabase.CreateFolder("Assets", "_RigTest");
    bool ok = AssetDatabase.CopyAsset("Assets/Character/Player.psb", T);
    var so = new SerializedObject(AssetImporter.GetAtPath(T));
    return "copied=" + ok + " mapping=" + so.FindProperty("m_LayerMappingOption").intValue + " | " + State();
  }
  public static string SwitchToName() { PlayerRigBuilder.UseLayerNameMapping(T); return "mapping=" + new SerializedObject(AssetImporter.GetAtPath(T)).FindProperty("m_LayerMappingOption").intValue + " | " + State(); }
  // Simulates a user edit in the Skinning Editor: move head joint +7px, drag one skirt vertex, set it 100% to bone 1.
  public static string Mark() {
    var dp = DP(T); var cp = dp.GetDataProvider<ICharacterDataProvider>(); var mp = dp.GetDataProvider<ISpriteMeshDataProvider>();
    var cd = cp.GetCharacterData(); int h = System.Array.FindIndex(cd.bones, b => b.name == "head");
    var hb = cd.bones[h]; hb.position += new Vector3(7, 0, 0); cd.bones[h] = hb; cp.SetCharacterData(cd);
    var skirt = dp.GetSpriteRects().First(r => r.name == "08_skirt");
    var v = mp.GetVertices(skirt.spriteID); v[5].position += new Vector2(3, 3); v[5].boneWeight = new BoneWeight { boneIndex0 = 1, weight0 = 1 };
    mp.SetVertices(skirt.spriteID, v);
    dp.Apply(); AssetDatabase.ImportAsset(T, ImportAssetOptions.ForceUpdate);
    return State();
  }
  public static string State() {
    var dp = DP(T); var cd = dp.GetDataProvider<ICharacterDataProvider>().GetCharacterData(); var mp = dp.GetDataProvider<ISpriteMeshDataProvider>();
    var head = cd.bones.FirstOrDefault(b => b.name == "head");
    var skirt = dp.GetSpriteRects().FirstOrDefault(r => r.name == "08_skirt"); var cap = dp.GetSpriteRects().FirstOrDefault(r => r.name == "17_cap");
    var v = skirt != null ? mp.GetVertices(skirt.spriteID) : new Vertex2DMetaData[0];
    var sprites = AssetDatabase.LoadAllAssetsAtPath(T).OfType<Sprite>().ToArray();
    var tex = AssetDatabase.LoadAllAssetsAtPath(T).OfType<Texture2D>().FirstOrDefault();
    var sb = new StringBuilder();
    sb.Append($"bones={cd.bones.Length} head.pos={head.position} skirtVerts={v.Length}");
    if (v.Length > 5) sb.Append($" v5={v[5].position} w5=({v[5].boneWeight.boneIndex0}:{v[5].boneWeight.weight0:F2})");
    sb.Append($" skirtRect={skirt?.rect} capRect={cap?.rect} skirtID={skirt?.spriteID}");
    sb.Append($" spritesWithBones={sprites.Count(s => s.GetBones().Length > 0)}/{sprites.Length}");
    var capSprite = sprites.FirstOrDefault(s => s.name == "17_cap");
    if (capSprite != null && tex != null) { var r = capSprite.textureRect; sb.Append(" capTexRect=" + r); }
    return sb.ToString();
  }
}
