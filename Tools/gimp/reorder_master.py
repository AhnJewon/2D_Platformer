# GIMP 3 batch script: reorder the master parts XCF to the Unity draw order (ArtSource/layer_order.json).
# Pixels, visibility, groups and hidden reference layers are kept; only the stacking order changes.
# Run via Tools/gimp_run.py reorder_master   (needs PROJECT_ROOT in the environment)
import json, os
from gi.repository import Gimp, Gio

root = os.environ['PROJECT_ROOT']
src = json.load(open(os.path.join(root, 'ArtSource', 'parts_source.json'), encoding='utf-8'))
master = os.path.join(root, src.get('master_xcf', src.get('xcf')))
order = json.load(open(os.path.join(root, 'ArtSource', 'layer_order.json'), encoding='utf-8'))['order']
stem = lambda n: n[:-4] if n.lower().endswith('.png') else n

img = Gimp.file_load(Gimp.RunMode.NONINTERACTIVE, Gio.File.new_for_path(master))
top = list(img.get_layers())
owner = {}  # part name -> top-level item that holds it
for item in top:
    if item.is_group():
        for ch in item.get_children(): owner[stem(ch.get_name())] = item
    else:
        owner[stem(item.get_name())] = item
target = []
for n in order:
    it = owner.get(n)
    if it is None: raise SystemExit('layer not found in master XCF: ' + n)
    if it not in target: target.append(it)
target += [it for it in top if it not in target]  # hidden reference layers etc. stay at the bottom
for pos, it in enumerate(target):
    img.reorder_item(it, None, pos)
for it in target:  # children of groups follow the same order
    if it.is_group():
        kids = sorted(it.get_children(), key=lambda c: order.index(stem(c.get_name())) if stem(c.get_name()) in order else 999)
        for pos, c in enumerate(kids): img.reorder_item(c, it, pos)
Gimp.file_save(Gimp.RunMode.NONINTERACTIVE, img, Gio.File.new_for_path(master), None)
img.delete()
