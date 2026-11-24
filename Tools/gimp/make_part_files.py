# GIMP 3 batch script (run by Tools/make_part_files.py): one XCF per part in ArtSource/Characters/Officer/Rig/Parts/.
# Each file: the part layer (named like the part) on top + a locked "REF ..." layer showing the other parts
# at low opacity for context. Layers whose name starts with "REF" are ignored by Tools/update_parts.py.
# Existing files are never overwritten.
import json, os
from gi.repository import Gimp, Gio

root = os.environ['PROJECT_ROOT']
tmp = os.environ['PARTS_TMP']
jobs = json.load(open(os.path.join(tmp, 'jobs.json'), encoding='utf-8'))
for job in jobs:
    out = job['out']
    if os.path.exists(out): continue
    img = Gimp.Image.new(job['w'], job['h'], Gimp.ImageBaseType.RGB)
    ref = Gimp.file_load_layer(Gimp.RunMode.NONINTERACTIVE, img, Gio.File.new_for_path(job['ref']))
    img.insert_layer(ref, None, 0)
    ref.set_name('REF other parts (ignored by Unity sync)')
    ref.set_opacity(30.0)
    ref.set_lock_content(True)
    ref.set_lock_position(True)
    part = Gimp.file_load_layer(Gimp.RunMode.NONINTERACTIVE, img, Gio.File.new_for_path(job['part']))
    img.insert_layer(part, None, 0)
    part.set_name(job['name'])
    img.set_selected_layers([part])
    img.clean_all()
    Gimp.file_save(Gimp.RunMode.NONINTERACTIVE, img, Gio.File.new_for_path(out), None)
    img.delete()
