"""Push edited loose sprites (ArtSource/Characters/Officer/Sprites/<name>.xcf: hand poses, sabre, slash, dummy) into Unity.

  python Tools/update_sprites.py              # every sprite in ArtSource/Characters/Officer/Sprites/sprites.json
  python Tools/update_sprites.py Hand_Grip    # only these
  python Tools/update_sprites.py --no-unity   # write the PNGs, do not ask the Editor to reimport

For each sprite: the visible layers except REF* are composited (Tools/xcf_read.py, no GIMP needed) and written to its
PNG in Assets/Characters/Officer at the XCF's canvas size, so the pivot stays where the REF pivot cross is. The import settings
(px/unit, pivot) come from sprites.json via Assets/Editor/WeaponSpriteImport.cs. Tools/watch_parts.py runs this
automatically when a file in ArtSource/Characters/Officer/Sprites is saved."""
import json, os, shutil, subprocess, sys
import numpy as np
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, 'Tools'))
import xcf_read

import project_paths as PP


def main(argv=None):
    argv = list(sys.argv[1:] if argv is None else argv)
    no_unity = '--no-unity' in argv
    names = [a for a in argv if not a.startswith('--')]
    entries = []
    for SPR in PP.sprite_sets():
        for name, m in json.load(open(os.path.join(SPR, 'sprites.json'), encoding='utf-8')).items(): entries.append((SPR, name, m))
    done = []
    for SPR, name, m in entries:
        if names and name not in names: continue
        xcf = os.path.join(SPR, name + '.xcf')
        if not os.path.exists(xcf): continue
        px = xcf_read.composite(xcf, skip=lambda n: n.strip().upper().startswith('REF'))
        flags = m.get('flags', [])
        if 'from_black' in flags or 'defringe_white' in flags:
            f = px.astype(np.float32) / 255.0
            rgb, a = f[..., :3], f[..., 3:4]
            if 'from_black' in flags:      # glow drawn on black: brightness becomes alpha, colour un-premultiplied
                lum = rgb.max(2, keepdims=True)
                a = a * np.clip((lum - 0.06) / 0.94, 0, 1)
                rgb = np.clip(rgb / np.maximum(lum, 1e-3), 0, 1)
            if 'defringe_white' in flags:  # drawn on white: un-blend the half-transparent edge from white
                edge = (a > 0.0) & (a < 1.0)
                rgb = np.where(edge, np.clip((rgb - (1.0 - a)) / np.maximum(a, 1e-3), 0, 1), rgb)
            px = (np.concatenate([rgb, a], -1) * 255 + 0.5).astype(np.uint8)
        im = Image.fromarray(px, 'RGBA')
        if list(im.size) != m['size']:
            print('%s: canvas is %dx%d, expected %dx%d - resizing the canvas moves the pivot; undo it in GIMP'
                  % (name, im.width, im.height, *m['size'])); continue
        dst = os.path.join(ROOT, m['dst'])
        im.save(dst)
        done.append(m['dst'])
        print('wrote', m['dst'])
    if not done or no_unity: return 0
    unity = shutil.which('unity')
    if not unity:
        print('Unity CLI not found: the Editor reimports the PNGs when it gets focus'); return 0
    code = ('foreach (var p in new[]{%s}) UnityEditor.AssetDatabase.ImportAsset(p, UnityEditor.ImportAssetOptions.ForceUpdate); '
            'return "reimported";') % ','.join('"%s"' % d for d in done)
    r = subprocess.run([unity, 'command', 'script', 'eval', '--project-path', ROOT, '--caller', 'plugin', '--skill', 'unity-cli', code],
                       capture_output=True, text=True, encoding='utf-8', errors='replace')
    print('Unity:', 'reimported' if 'reimported' in r.stdout else 'Editor not reachable (reimports on focus)')
    return 0


if __name__ == '__main__':
    sys.exit(main())
