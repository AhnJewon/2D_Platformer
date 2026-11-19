# GIMP 3 batch script (run by Tools/make_sprite_files.py): one XCF per loose sprite in ArtSource/Sprites/.
# Each file: the sprite layer (named like the sprite: the NovelAI original + a layer mask) on top + locked "REF ..." layers for context (pivot cross, sleeve,
# sabre...). Layers whose name starts with "REF" are ignored by Tools/update_sprites.py. Existing files are never
# overwritten.
import json, os
from gi.repository import Gimp, Gio

tmp = os.environ['SPRITES_TMP']
jobs = json.load(open(os.path.join(tmp, 'jobs.json'), encoding='utf-8'))
for job in jobs:
    out = job['out']
    if os.path.exists(out): continue
    img = Gimp.Image.new(job['w'], job['h'], Gimp.ImageBaseType.RGB)
    for L in job['layers']:   # bottom first
        layer = Gimp.file_load_layer(Gimp.RunMode.NONINTERACTIVE, img, Gio.File.new_for_path(L['png']))
        img.insert_layer(layer, None, 0)
        layer.set_name(L['name'])
        layer.set_opacity(float(L.get('opacity', 100.0)))
        if L.get('locked'):
            layer.set_lock_content(True)
            layer.set_lock_position(True)
        if L.get('mask_transfer'):
            # the PNG holds the untouched original RGB with the cut-out as alpha: move that alpha into a layer mask,
            # so the layer keeps every original pixel and the cut-out is editable (paint the mask white to restore)
            mask = layer.create_mask(Gimp.AddMaskType.ALPHA_TRANSFER)
            layer.add_mask(mask)
        if L.get('main'): main = layer
    img.set_selected_layers([main])
    img.clean_all()
    Gimp.file_save(Gimp.RunMode.NONINTERACTIVE, img, Gio.File.new_for_path(out), None)
    img.delete()
