"""Re-import repainted character parts into Assets/Character/Player.psb WITHOUT touching the rig.

  uv run --no-project --with numpy --with pillow python Tools/update_parts.py            # use ArtSource/parts_source.json
  ... update_parts.py --source xcf  --xcf  ArtSource/NAI_V5_rig_v3/parts_canvas/UNASSIGNED_review_needed.xcf
  ... update_parts.py --source pngs --pngs ArtSource/NAI_V5_rig_v3/parts_canvas          # <part>.png, full 832x1216 canvas
Run it from the project folder. Relative paths are resolved against the project root.
  ... update_parts.py --dry-run                                                           # report only, change nothing

What it does
 1. Reads the parts (XCF layers, or one full-canvas PNG per part named like the layer, e.g. 17_cap.png).
 2. Checks the part names match ArtSource/layer_order.json (a new/renamed/removed part needs a rig change: it stops).
 3. Backs up Player.psb and Player.psb.meta (the meta holds your bones/mesh/weights) to ArtSource/backups/<time>/.
 4. Writes the new layer PNGs to ArtSource/layers and rebuilds Player.psb with the SAME layer names, order and
    locked rects (ArtSource/layer_bounds.json), then asks the open Unity Editor to reimport it.
Unity matches layers by name, so each sprite keeps its bones, mesh and weights.
If a part was painted OUTSIDE its locked rect, the rect grows: the script enlarges that sprite and shifts its existing
mesh/bones so your skinning stays in place (PlayerRigBuilder.RefitGrownParts). The newly painted area has no mesh
yet, so it is not drawn: extend that sprite's mesh in the Skinning Editor.
"""
import argparse, datetime, json, os, shutil, subprocess, sys, tempfile
import numpy as np
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ART = os.path.join(ROOT, 'ArtSource')
PSB = os.path.join(ROOT, 'Assets', 'Character', 'Player.psb')
TOOLS = os.path.join(ROOT, 'Tools')
REIMPORT_CS = 'Tools/reimport_player.cs'  # relative to the project root


def load(p): return json.load(open(p, encoding='utf-8'))
def stem(n): return os.path.splitext(n)[0]


def read_pngs(folder, names, out):
    layers, canvas = [], None
    for i, n in enumerate(names):
        p = os.path.join(folder, n + '.png')
        if not os.path.exists(p): raise SystemExit('missing part image: ' + p)
        im = Image.open(p).convert('RGBA')
        if canvas is None: canvas = list(im.size)
        if list(im.size) != canvas: raise SystemExit('%s is %s, expected full canvas %s' % (p, im.size, canvas))
        fn = 'L%02d.png' % i
        im.save(os.path.join(out, fn))
        layers.append(dict(index=i, name=n + '.png', visible=True, group=False, file=fn, bbox=list(im.getbbox()) if im.getbbox() else None))
    json.dump(dict(canvas=canvas, layers=layers), open(os.path.join(out, 'layers.json'), 'w', encoding='utf-8'), ensure_ascii=False, indent=1)


def parts_of(layers_dir):
    m = load(os.path.join(layers_dir, 'layers.json'))
    return {stem(l['name']): l for l in m['layers'] if l.get('file') and l['visible'] and l.get('bbox')}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--source', choices=['xcf', 'pngs'])
    ap.add_argument('--xcf'); ap.add_argument('--pngs')
    ap.add_argument('--dry-run', action='store_true')
    ap.add_argument('--no-unity', action='store_true', help='do not ask the Editor to reimport')
    a = ap.parse_args()

    cfg_path = os.path.join(ART, 'parts_source.json')
    cfg = load(cfg_path) if os.path.exists(cfg_path) else {}
    source = a.source or cfg.get('source', 'xcf')
    order = load(os.path.join(ART, 'layer_order.json'))['order']

    staging = tempfile.mkdtemp(prefix='parts_', dir=ART)
    try:
        if source == 'xcf':
            xcf = a.xcf or cfg.get('xcf')
            xcf = xcf and os.path.join(ROOT, xcf)
            if not xcf or not os.path.exists(xcf): raise SystemExit('XCF not found: %s' % xcf)
            subprocess.run([sys.executable, '-I', os.path.join(TOOLS, 'xcf_extract.py'), xcf, staging], check=True, stdout=subprocess.DEVNULL)
        else:
            folder = a.pngs or cfg.get('pngs')
            folder = folder and os.path.join(ROOT, folder)
            if not folder or not os.path.isdir(folder): raise SystemExit('PNG folder not found: %s' % folder)
            read_pngs(folder, order, staging)

        new, old = parts_of(staging), parts_of(os.path.join(ART, 'layers'))
        if set(new) != set(order):
            raise SystemExit('Part names differ from ArtSource/layer_order.json\n  new parts: %s\n  missing parts: %s\n'
                             'Adding/renaming/removing a part needs a rig change (bone_layout.json + layer_order.json).'
                             % (sorted(set(new) - set(order)), sorted(set(order) - set(new))))

        bounds_doc = load(os.path.join(ART, 'layer_bounds.json'))
        bounds = bounds_doc['bounds']
        changed, grown = [], []
        for n in order:
            nb = new[n]['bbox']
            A = np.asarray(Image.open(os.path.join(staging, new[n]['file'])).convert('RGBA'), np.int16)
            B = np.asarray(Image.open(os.path.join(ART, 'layers', old[n]['file'])).convert('RGBA'), np.int16)
            diff = int(((np.abs(A - B).max(2) > 2) & ((A[..., 3] > 0) | (B[..., 3] > 0))).sum())
            if diff: changed.append((n, diff))
            lb = bounds[n]
            if nb[0] < lb[0] or nb[1] < lb[1] or nb[2] > lb[2] or nb[3] > lb[3]:
                bounds[n] = [min(nb[0], lb[0]), min(nb[1], lb[1]), max(nb[2], lb[2]), max(nb[3], lb[3])]
                grown.append((n, lb, bounds[n]))

        print('source:', source)
        print('changed parts:', ', '.join('%s (%d px)' % c for c in changed) if changed else 'none')
        for n, lb, nb in grown:
            print('RECT GROWN  %s %s -> %s : extend this sprite\'s mesh in the Skinning Editor afterwards' % (n, lb, nb))
        if not changed:
            print('Nothing to update.'); return
        if a.dry_run:
            print('dry run: nothing written'); return

        stamp = datetime.datetime.now().strftime('%Y%m%d-%H%M%S')
        bdir = os.path.join(ART, 'backups', stamp)
        os.makedirs(bdir)
        for f in (PSB, PSB + '.meta'): shutil.copy2(f, bdir)
        print('backup:', os.path.relpath(bdir, ROOT))

        shutil.rmtree(os.path.join(ART, 'layers'))
        shutil.copytree(staging, os.path.join(ART, 'layers'))
        if grown: json.dump(bounds_doc, open(os.path.join(ART, 'layer_bounds.json'), 'w', encoding='utf-8'), indent=1)

        subprocess.run([sys.executable, '-I', os.path.join(TOOLS, 'build_psb.py'), os.path.join(ART, 'layers'), PSB,
                        '--order', os.path.join(ART, 'layer_order.json'), '--bounds', os.path.join(ART, 'layer_bounds.json')], check=True)

        refit = os.path.join(ART, 'pending_refit.json')
        if grown:
            parts = [dict(name=n, growLeft=lb[0] - nb[0], growBottom=nb[3] - lb[3], width=nb[2] - nb[0], height=nb[3] - nb[1])
                     for n, lb, nb in grown]
            json.dump(dict(parts=parts), open(refit, 'w', encoding='utf-8'), indent=1)

        if a.no_unity:
            print('Unity: skipped (--no-unity)')
        else:
            unity = shutil.which('unity')
            r = subprocess.run([unity, 'command', 'script', 'eval', '--project-path', ROOT, '--timeout', '300',
                                '--caller', 'plugin', '--skill', 'unity-cli', '--file', REIMPORT_CS],
                               capture_output=True, text=True) if unity else None
            if r is not None and 'reimported' in r.stdout:
                print('Unity:', r.stdout.strip().strip('"').replace('\\n', '\n'))
            else:
                print('Unity: Editor not reachable. Unity reimports the PSB when it next opens/gets focus.')
        if os.path.exists(refit):
            print('PENDING: grown parts are not refitted yet. With the Unity Editor open, run:\n'
                  '  unity command script eval --project-path "%s" --file Tools/reimport_player.cs' % ROOT)
    finally:
        shutil.rmtree(staging, ignore_errors=True)


if __name__ == '__main__':
    main()
