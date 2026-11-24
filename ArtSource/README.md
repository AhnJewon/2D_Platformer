# ArtSource layout

All paths for the tools are defined in `Tools/project_paths.py`.

```
ArtSource/
  Characters/<Name>/          one folder per character (the first is Officer)
    Rig/                      what the rigged character (Player.psb) is built from
      Parts/*.xcf             one GIMP file per part; save to sync (Tools/watch_parts.py)
      layers/                 PSB layer PNGs (written by Tools/update_parts.py)
      layer_order.json, layer_bounds.json, bone_layout.json, parts_source.json, guides/
    Sprites/                  loose sprites of this character: hand poses, expressions (<name>.xcf + sprites.json)
    Source/                   NovelAI originals and intermediates (original art, part splits, slide art, hands, face)
  Weapons/  Sprites/ + Source/    weapon art and effects (shared by every character)
  Props/    Sprites/ + Source/    training dummy, items
  References/                 motion and game references (shared)
  backups/                    automatic backups (not committed)
```

Unity side: `Assets/Characters/<Name>/` (Player.psb, Player.prefab, Animations, Hands, Face, Slide), `Assets/Weapons/`,
`Assets/Props/`.

Sprite XCFs keep the untouched NovelAI original as the layer and the cut-out as a layer mask. `REF` layers are
references and are ignored by the sync. Each `sprites.json` gives the destination PNG, px/unit and pivot. The Unity
importer (`Assets/Editor/WeaponSpriteImport.cs`) reads them from every `Sprites/` folder.

A new character gets its own `Characters/<Name>/` with the same sub-folders. Run the tools with `CHARACTER=<Name>`.
