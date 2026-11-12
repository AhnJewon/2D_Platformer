using System.IO; using System.Linq; using UnityEditor; using UnityEditor.U2D.Animation; using UnityEditor.U2D.Sprites; using UnityEngine;
// Every sprite's position/size in the PSB must equal its locked rect in ArtSource/layer_bounds.json.
public static class CheckPartPositions {
  [System.Serializable] class B { public string n; }
  public static string Run() {
    var root = Path.GetDirectoryName(Application.dataPath);
    var json = File.ReadAllText(Path.Combine(root, "ArtSource/layer_bounds.json"));
    var f = new SpriteDataProviderFactories(); f.Init(); var dp = f.GetSpriteEditorDataProviderFromObject(AssetImporter.GetAtPath("Assets/Character/Player.psb")); dp.InitSpriteEditorDataProvider();
    var cd = dp.GetDataProvider<ICharacterDataProvider>().GetCharacterData();
    int bad = 0; var sb = new System.Text.StringBuilder();
    foreach (var r in dp.GetSpriteRects()) {
      int i = json.IndexOf("\"" + r.name + "\""); var nums = json.Substring(json.IndexOf('[', i) + 1, json.IndexOf(']', i) - json.IndexOf('[', i) - 1).Split(',').Select(s => int.Parse(s.Trim())).ToArray();
      var p = cd.parts.First(x => x.spriteId == r.spriteID.ToString()).spritePosition;
      int ex = nums[0], ey = 1216 - nums[3], ew = nums[2] - nums[0], eh = nums[3] - nums[1];
      if (p.x != ex || p.y != ey || (int)r.rect.width != ew || (int)r.rect.height != eh) { bad++; sb.Append($"{r.name}: pos=({p.x},{p.y}) size={r.rect.width}x{r.rect.height} expected ({ex},{ey}) {ew}x{eh}\n"); }
    }
    return (bad == 0 ? "all sprites match their locked rects" : bad + " mismatched\n") + sb;
  }
}
