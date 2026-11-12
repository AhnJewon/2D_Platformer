"""Layer PNGs (layers.json + Lxx.png from xcf_extract.py) -> layered PSB for Unity PSD Importer.

Usage: python build_psb.py <layers_dir> <out.psb> [--order layer_order.json] [--bounds layer_bounds.json]

- Uses visible, non-group layers. XCF order is top-first; PSB records are written bottom-first.
- --order: "order" list (top-first, names without .png) replaces the XCF order.
- --bounds: fixed layer rects {"name": [x0, y0, x1, y1]} in canvas pixels (y down). Each layer is written
  with that rect (transparent padding) so Unity's sprite rect never changes when you repaint a part.
  Content outside the rect is an error: update_parts.py grows the rect first and reports it.
"""
import argparse, json, os, struct
import numpy as np
from PIL import Image

ap = argparse.ArgumentParser()
ap.add_argument('src'); ap.add_argument('out')
ap.add_argument('--order'); ap.add_argument('--bounds')
args = ap.parse_args()

meta = json.load(open(os.path.join(args.src, 'layers.json'), encoding='utf-8'))
W, H = meta['canvas']
layers = [l for l in meta['layers'] if l.get('file') and l['visible'] and l.get('bbox')]
stem = lambda l: os.path.splitext(l['name'])[0]
if args.order:
    order = json.load(open(args.order, encoding='utf-8'))['order']
    by_name = {stem(l): l for l in layers}
    diff = set(by_name) ^ set(order)
    if diff: raise SystemExit('layer_order.json does not match the layers: %s' % sorted(diff))
    layers = [by_name[n] for n in order]
bounds = json.load(open(args.bounds, encoding='utf-8'))['bounds'] if args.bounds else {}
layers = list(reversed(layers))  # bottom -> top

recs, chans = bytearray(), bytearray()
comp = Image.new('RGBA', (W, H), (0, 0, 0, 0))
for l in layers:
    im = Image.open(os.path.join(args.src, l['file'])).convert('RGBA')
    comp.alpha_composite(im)
    x0, y0, x1, y1 = bounds.get(stem(l), l['bbox'])
    bx0, by0, bx1, by1 = l['bbox']
    if bx0 < x0 or by0 < y0 or bx1 > x1 or by1 > y1:
        raise SystemExit('%s: content %s is outside its locked bounds %s' % (stem(l), l['bbox'], [x0, y0, x1, y1]))
    a = np.asarray(im)[y0:y1, x0:x1].copy()
    # Unity's packer trims fully transparent borders, which would move the sprite away from its locked rect
    # (and from the mesh/bones made for it) when a part is repainted smaller. An invisible pixel
    # (alpha 1/255) in each empty corner keeps the trimmed size equal to the locked rect.
    for cy, cx in ((0, 0), (0, -1), (-1, 0), (-1, -1)):
        if a[cy, cx, 3] == 0: a[cy, cx] = (0, 0, 0, 1)
    name = stem(l).encode('ascii')
    planes = [(-1, a[..., 3]), (0, a[..., 0]), (1, a[..., 1]), (2, a[..., 2])]
    r = struct.pack('>iiiiH', y0, x0, y1, x1, 4)
    for cid, pl in planes:
        r += struct.pack('>hQ', cid, pl.size + 2)
        chans += struct.pack('>H', 0) + np.ascontiguousarray(pl).tobytes()
    pname = bytes([len(name)]) + name
    pname += b'\0' * ((4 - len(pname) % 4) % 4)
    extra = struct.pack('>I', 0) + struct.pack('>I', 0) + pname
    r += b'8BIMnorm' + bytes([255, 0, 0, 0]) + struct.pack('>I', len(extra)) + extra
    recs += r

layer_info = struct.pack('>h', -len(layers)) + recs + chans
if len(layer_info) % 2: layer_info += b'\0'
lmi = struct.pack('>Q', len(layer_info)) + layer_info + struct.pack('>I', 0)
c = np.asarray(comp)
tmp = args.out + '.tmp'
with open(tmp, 'wb') as f:
    f.write(b'8BPS' + struct.pack('>H', 2) + b'\0' * 6 + struct.pack('>HIIHH', 4, H, W, 8, 3))
    f.write(struct.pack('>I', 0) + struct.pack('>I', 0))
    f.write(struct.pack('>Q', len(lmi)) + lmi)
    f.write(struct.pack('>H', 0))
    for ch in (0, 1, 2, 3): f.write(c[..., ch].tobytes())
os.replace(tmp, args.out)
print('wrote', args.out, len(layers), 'layers')
