using System.IO;
using UnityEditor;
using UnityEngine;

// Import settings for the loose sprites: Assets/Weapons/*.png, Assets/Props/*.png and each character's
// Assets/Characters/<Name>/Hands|Face/*.png.
// Source of truth: <set>/Sprites/<name>.xcf under ArtSource (Characters/<Name>, Weapons, Props), written to the PNG by
// Tools/update_sprites.py. That set's sprites.json gives each sprite's px/unit and its pivot in canvas pixels
// (the REF pivot cross in the XCF): wrist for hands, middle of the grip for the sabre, centre of the swing for the
// slash, bottom of the base for the dummy. The values below are only used for a PNG that is not listed there.
public class WeaponSpriteImport : AssetPostprocessor
{
    [System.Serializable] class Entry { public string dst; public float ppu; public float[] pivot; public int[] size; }

    static bool FromJson(string assetPath, out float ppu, out Vector2 pivot)
    {
        ppu = 400f; pivot = new Vector2(0.5f, 0.5f);
        string art = Path.Combine(Path.GetDirectoryName(Application.dataPath), "ArtSource");
        if (!Directory.Exists(art)) return false;
        // tiny reader for {"Name": {"dst": "...", "ppu": n, "pivot": [x, y], "size": [w, h]}, ...}; the entry whose dst is
        // this asset, in any sprites.json (skipping backups)
        string json = null; int i = -1;
        foreach (var path in Directory.GetFiles(art, "sprites.json", SearchOption.AllDirectories))
        {
            if (path.Replace('\\', '/').Contains("/backups/")) continue;
            var text = File.ReadAllText(path);
            int k = text.IndexOf("\"" + assetPath + "\"");
            if (k >= 0) { json = text; i = k; break; }
        }
        if (json == null) return false;
        int start = json.LastIndexOf('{', i), end = json.IndexOf('}', i);
        var e = JsonUtility.FromJson<Entry>(json.Substring(start, end - start + 1));
        if (e == null || e.pivot == null || e.size == null || e.size.Length < 2) return false;
        ppu = e.ppu;
        pivot = new Vector2(e.pivot[0] / e.size[0], 1f - e.pivot[1] / e.size[1]);
        return true;
    }

    void OnPreprocessTexture()
    {
        bool hands = assetPath.StartsWith("Assets/Characters/") && (assetPath.Contains("/Hands/") || assetPath.Contains("/Face/"));
        if (!assetPath.StartsWith("Assets/Weapons/") && !assetPath.StartsWith("Assets/Props/") && !hands) return;
        var ti = (TextureImporter)assetImporter;
        ti.textureType = TextureImporterType.Sprite;
        ti.spriteImportMode = SpriteImportMode.Single;
        ti.mipmapEnabled = false;
        ti.alphaIsTransparency = true;
        var s = new TextureImporterSettings();
        ti.ReadTextureSettings(s);
        s.spriteAlignment = (int)SpriteAlignment.Custom;
        s.spriteMeshType = SpriteMeshType.Tight;
        string f = Path.GetFileNameWithoutExtension(assetPath);
        if (f == "Slash") s.spriteMeshType = SpriteMeshType.FullRect;
        s.readable = f == "Pistol";   // GunCellGauge recolours the energy cell at runtime
        if (FromJson(assetPath, out float ppu, out Vector2 pivot))
        {
            s.spritePixelsPerUnit = ppu; s.spritePivot = pivot;
        }
        else if (hands)
        {
            s.spritePixelsPerUnit = 400f;
            s.spritePivot = f == "Hand_GripInline" ? new Vector2(0.0222f, 0.4824f) : new Vector2(0.5f, 0.5f);
        }
        else if (f == "Saber") { s.spritePixelsPerUnit = 400f; s.spritePivot = new Vector2(0.1397f, 0.4635f); }
        else if (f == "Slash") { s.spritePixelsPerUnit = 190f; s.spritePivot = new Vector2(0.5f, 0.5f); }
        else if (f == "Dummy") { s.spritePixelsPerUnit = 330f; s.spritePivot = new Vector2(0.5f, 0.004f); }
        else s.spritePixelsPerUnit = 400f;
        ti.SetTextureSettings(s);
    }
}
