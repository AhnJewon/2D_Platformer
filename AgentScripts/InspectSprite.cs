using System.Linq; using UnityEditor; using UnityEditor.U2D.Animation; using UnityEditor.U2D.Sprites; using UnityEngine;
public static class InspectSprite {
  public static string Run() {
    string psb = "Assets/Characters/Officer/Player.psb"; var sb = new System.Text.StringBuilder();
    var f = new SpriteDataProviderFactories(); f.Init(); var dp = f.GetSpriteEditorDataProviderFromObject(AssetImporter.GetAtPath(psb)); dp.InitSpriteEditorDataProvider();
    var cd = dp.GetDataProvider<ICharacterDataProvider>().GetCharacterData(); var mp = dp.GetDataProvider<ISpriteMeshDataProvider>();
    foreach (var n in new[]{"15_head_face","15b_neck","17_cap"}) {
      var r = dp.GetSpriteRects().First(x => x.name == n); var part = cd.parts.First(p => p.spriteId == r.spriteID.ToString());
      var v = mp.GetVertices(r.spriteID); var ys = v.Select(x => x.position.y); var xs = v.Select(x => x.position.x);
      var s = AssetDatabase.LoadAllAssetsAtPath(psb).OfType<Sprite>().First(x => x.name == n);
      sb.Append($"{n}: rect={r.rect} partPos={part.spritePosition} mesh x[{xs.Min()}..{xs.Max()}] y[{ys.Min()}..{ys.Max()}] verts={v.Length} | sprite.rect={s.rect} textureRect={s.textureRect}\n");
    }
    return sb.ToString();
  }
}
