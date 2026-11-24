using UnityEditor;
using UnityEngine;

// Import settings for the hand-drawn slide frames (Assets/Characters/Officer/Slide/*.png): same scale as the rig (400 px/unit)
// and a pivot under the hips, so swapping between the rig and the sprite keeps her in place.
// Source: the original pose baseball_slide_sketch_pose_rgba.png (1376x768, Tools/import_slide_frames.py). It is
// ~0.864x the rig's pixel scale (cap ~270 px wide vs 317 px on the rig), so 400 * 0.864 = 346 px/unit keeps her size.
// Pivot: under the hips, minus the rig's hip offset -> x = 682 px; soles 72 px above the bottom and the rig root sits
// 0.08 units (28 px) under the soles -> y = 43 px. (The half-size 4-frame sheet would use 173 px/unit, pivot 341,21.)
public class SlideSpriteImport : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        if (!(assetPath.StartsWith("Assets/Characters/") && assetPath.Contains("/Slide/"))) return;
        var ti = (TextureImporter)assetImporter;
        ti.textureType = TextureImporterType.Sprite;
        ti.spriteImportMode = SpriteImportMode.Single;
        ti.spritePixelsPerUnit = 407f;   // 346 = rig size; 407 = 85% of it (reads better next to the rig)
        ti.mipmapEnabled = false;
        ti.alphaIsTransparency = true;
        var s = new TextureImporterSettings();
        ti.ReadTextureSettings(s);
        s.spriteAlignment = (int)SpriteAlignment.Custom;
        s.spritePivot = new Vector2(682f / 1376f, 67f / 768f);   // 43 = soles on the floor; 67 lowers her 0.06 u so the hips/coat sit near the floor (dust hides the hand and heels)
        s.spriteMeshType = SpriteMeshType.Tight;
        ti.SetTextureSettings(s);
    }
}
