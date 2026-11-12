"""Create ArtSource/Parts/<part>.xcf (one GIMP file per part) from the current ArtSource/layers.
Existing part files are left untouched. Usage: python Tools/make_part_files.py"""
import json, os, shutil, sys, tempfile
import numpy as np
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, 'Tools'))
import gimp_run

ART = os.path.join(ROOT, 'ArtSource')
src = json.load(open(os.path.join(ART, 'parts_source.json'), encoding='utf-8'))
parts_dir = os.path.join(ROOT, src['parts'])
os.makedirs(parts_dir, exist_ok=True)
order = json.load(open(os.path.join(ART, 'layer_order.json'), encoding='utf-8'))['order']
meta = json.load(open(os.path.join(ART, 'layers', 'layers.json'), encoding='utf-8'))
W, H = meta['canvas']
files = {os.path.splitext(l['name'])[0]: os.path.join(ART, 'layers', l['file']) for l in meta['layers'] if l.get('file')}
imgs = {n: Image.open(files[n]).convert('RGBA') for n in order}

tmp = tempfile.mkdtemp(prefix='parts_')
try:
    jobs = []
    for n in order:
        out = os.path.join(parts_dir, n + '.xcf')
        if os.path.exists(out): continue
        ref = Image.new('RGBA', (W, H), (0, 0, 0, 0))
        for m in reversed(order):
            if m != n: ref.alpha_composite(imgs[m])
        ref_p, part_p = os.path.join(tmp, n + '_ref.png'), os.path.join(tmp, n + '.png')
        ref.save(ref_p); imgs[n].save(part_p)
        jobs.append(dict(name=n, out=out, ref=ref_p, part=part_p, w=W, h=H))
    if not jobs:
        print('all part files already exist'); sys.exit(0)
    json.dump(jobs, open(os.path.join(tmp, 'jobs.json'), 'w', encoding='utf-8'))
    os.environ['PARTS_TMP'] = tmp
    ok = gimp_run.run('make_part_files')
    made = [j['name'] for j in jobs if os.path.exists(j['out'])]
    print('created %d part files in %s' % (len(made), os.path.relpath(parts_dir, ROOT)))
    sys.exit(0 if ok and len(made) == len(jobs) else 1)
finally:
    shutil.rmtree(tmp, ignore_errors=True)
