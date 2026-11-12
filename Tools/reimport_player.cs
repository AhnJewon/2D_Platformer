// Run by Tools/update_parts.py through `unity command script eval --file Tools/reimport_player.cs`.
// Reimports Player.psb, then refits parts whose locked rect grew (ArtSource/pending_refit.json), keeping their skinning in place.
var psb = "Assets/Character/Player.psb";
var pending = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(UnityEngine.Application.dataPath), "ArtSource", "pending_refit.json");
UnityEditor.AssetDatabase.ImportAsset(psb, UnityEditor.ImportAssetOptions.ForceUpdate);
var log = "";
if (System.IO.File.Exists(pending))
{
    log = PlayerRigBuilder.RefitGrownParts(psb, pending);
    System.IO.File.Delete(pending);
}
return "reimported " + log;
