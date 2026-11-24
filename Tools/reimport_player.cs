// Run by Tools/update_parts.py through `unity command script eval --file Tools/reimport_player.cs`.
// Reimports Player.psb, then
//  - skins parts that were just added (ArtSource/Characters/Officer/Rig/pending_skin.json), using the current skeleton
//  - refits parts whose locked rect grew (ArtSource/Characters/Officer/Rig/pending_refit.json), keeping their skinning in place.
var psb = "Assets/Characters/Officer/Player.psb";
var art = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(UnityEngine.Application.dataPath), "ArtSource", "Characters", "Officer", "Rig");
var skin = System.IO.Path.Combine(art, "pending_skin.json");
var refit = System.IO.Path.Combine(art, "pending_refit.json");
UnityEditor.AssetDatabase.ImportAsset(psb, UnityEditor.ImportAssetOptions.ForceUpdate);
var log = "";
if (System.IO.File.Exists(skin))
{
    log += PlayerRigBuilder.SkinNewParts(psb, skin) + "\n";
    System.IO.File.Delete(skin);
}
if (System.IO.File.Exists(refit))
{
    log += PlayerRigBuilder.RefitGrownParts(psb, refit);
    System.IO.File.Delete(refit);
}
return "reimported " + log;
