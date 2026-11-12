using System.IO; using System.Linq; using UnityEditor; using UnityEditor.U2D.Animation; using UnityEditor.U2D.Sprites; using UnityEngine;
// Writes the current character skeleton (head/tail in canvas pixels, y down) to ArtSource/.bones_current.json.
public static class DumpBones {
  public static string Run() {
    var f = new SpriteDataProviderFactories(); f.Init(); var dp = f.GetSpriteEditorDataProviderFromObject(AssetImporter.GetAtPath("Assets/Character/Player.psb")); dp.InitSpriteEditorDataProvider();
    var cb = dp.GetDataProvider<ICharacterDataProvider>().GetCharacterData().bones; int H = 1216;
    var wp = new Vector2[cb.Length]; var wr = new float[cb.Length]; var sb = new System.Text.StringBuilder("{\"bones\":[\n");
    for (int i = 0; i < cb.Length; ++i) {
      float lr = cb[i].rotation.eulerAngles.z;
      if (cb[i].parentId < 0) { wp[i] = cb[i].position; wr[i] = lr; } else { int p = cb[i].parentId; wp[i] = wp[p] + (Vector2)(Quaternion.Euler(0,0,wr[p]) * (Vector2)cb[i].position); wr[i] = wr[p] + lr; }
      var t = wp[i] + (Vector2)(Quaternion.Euler(0,0,wr[i]) * new Vector2(cb[i].length, 0));
      sb.Append($" {{\"name\":\"{cb[i].name}\",\"parent\":\"{(cb[i].parentId>=0?cb[cb[i].parentId].name:"")}\",\"head\":[{wp[i].x:F1},{H-wp[i].y:F1}],\"tail\":[{t.x:F1},{H-t.y:F1}]}}{(i<cb.Length-1?",":"")}\n");
    }
    sb.Append("]}");
    File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "ArtSource/.bones_current.json"), sb.ToString());
    return sb.ToString();
  }
}
