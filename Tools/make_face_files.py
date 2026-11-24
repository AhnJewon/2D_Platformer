"""Create ArtSource/Characters/Officer/Sprites/<name>.xcf for face swaps (expressions) and add them to ArtSource/Characters/Officer/Sprites/sprites.json,
so they sync like the other loose sprites (Tools/update_sprites.py, Tools/watch_parts.py -> Assets/Characters/Officer/Face).
Existing XCF files are left untouched.

Each file is a 200x200 crop of the character canvas around the eye, at the rig's scale (400 px/unit):
  <name>              the expression drawing (a placeholder to repaint)
  REF face            the face, bangs, cap, ear and side hair as they are now (locked, 50%)
  REF original eye    the normal eye parts (18_eye_main + 19_eye_upper_detail), to match position and size (35%)
  REF pivot           cross at the centre of the normal eye: the game puts the drawing's pivot there
In game PlayerHurt hides the rig's eye parts and shows the drawing on the head bone while the expression plays.
Usage: python Tools/make_face_files.py"""
import json, os, shutil, sys, tempfile
import numpy as np
from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, 'Tools'))
import gimp_run

import project_paths as PP
ART = PP.rig_dir()                 # layers of the character
OUT = PP.sprites_dir()
CROP = (400, 120, 600, 320)          # canvas px (x0, y0, x1, y1)
FACE = ['15b_neck', '15_head_face', '15_ear', '16_hair_front_near', '20_bangs_front', '17_cap']   # bottom -> top
EYE = ['18_eye_main', '19_eye_upper_detail']
INK = (34, 28, 40, 255)

meta_l = json.load(open(os.path.join(ART, 'layers', 'layers.json'), encoding='utf-8'))
files = {os.path.splitext(l['name'])[0]: os.path.join(ART, 'layers', l['file']) for l in meta_l['layers'] if l.get('file')}
def layer(n): return Image.open(files[n]).convert('RGBA').crop(CROP)


def stack(names):
    im = Image.new('RGBA', (CROP[2] - CROP[0], CROP[3] - CROP[1]), (0, 0, 0, 0))
    for n in names: im.alpha_composite(layer(n))
    return im


def eye_box():
    a = np.asarray(layer('18_eye_main'))[..., 3]
    ys, xs = np.nonzero(a > 0)
    return xs.min(), ys.min(), xs.max(), ys.max()


def hurt_placeholder(box):
    """squeezed-shut eye, '>' pointing toward the nose (+X), with two short lashes; drawn at 4x then reduced"""
    S = 4
    W, H = CROP[2] - CROP[0], CROP[3] - CROP[1]
    im = Image.new('RGBA', (W * S, H * S), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
    x0, y0, x1, y1 = box
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    tip = (x1 - 12, cy + 1)
    up, lo = (x0 + 22, cy - 9), (x0 + 24, cy + 10)
    w = 4
    for a, b in ((up, tip), (tip, lo)):
        d.line([(a[0] * S, a[1] * S), (b[0] * S, b[1] * S)], fill=INK, width=w * S)
    for p in (up, tip, lo):
        d.ellipse([(p[0] - w / 2) * S, (p[1] - w / 2) * S, (p[0] + w / 2) * S, (p[1] + w / 2) * S], fill=INK)
    for k in (0.0, 0.35):   # lashes on the upper stroke, sticking up-back
        px, py = up[0] + (tip[0] - up[0]) * k, up[1] + (tip[1] - up[1]) * k
        d.line([(px * S, py * S), ((px - 5) * S, (py - 6) * S)], fill=INK, width=2 * S)
    return im.resize((W, H), Image.LANCZOS)


def cross(w, h, p, r=10):
    im = Image.new('RGBA', (w, h), (0, 0, 0, 0)); d = ImageDraw.Draw(im); x, y = p
    d.line([(x - r, y), (x + r, y)], fill=(255, 40, 40, 255), width=2); d.line([(x, y - r), (x, y + r)], fill=(255, 40, 40, 255), width=2)
    return im


NAI = os.path.join(PP.source_dir(), 'face')
# NAI V5 + wiki preset (nai-v5-yamada9000-fis-mix), transparent background: hurt face in profile, seed 50101.
# The layer keeps the generated face as it is (only scaled so its eye matches the rig's eye, and moved so the eye
# centres line up); a layer mask keeps the near-black eye and brow strokes (the bangs outlines are brown, not black).
HURT_SRC = os.path.join(NAI, 'hurt_face_naiv5_preset_seed50101.png')
# the '>' tip (toward the nose) goes on the front corner of the normal eye; 64 px wide keeps the line weight close to the rig
HURT_EYE = dict(box=(673, 407, 848, 584), tip=(838, 537), width=170, scale_to=64, target=(522, 224))   # source / canvas px


def hurt_from_nai(box):
    """returns (layer RGBA, mask L): the scaled NAI face and the eye-stroke mask, both canvas-sized"""
    W, H = CROP[2] - CROP[0], CROP[3] - CROP[1]
    src = Image.open(HURT_SRC).convert('RGBA')
    k = HURT_EYE['scale_to'] / HURT_EYE['width']
    big = src.resize((round(src.width * k), round(src.height * k)), Image.LANCZOS)
    a = np.asarray(src).astype(np.float32)
    lum = a[..., :3].max(2)
    m = np.clip((75.0 - lum) / 45.0, 0, 1) * (a[..., 3] / 255.0)
    x0, y0, x1, y1 = HURT_EYE['box']
    keep = np.zeros_like(m); keep[y0:y1, x0:x1] = 1
    # eyes only: the generated brow (a separate stroke above) is dropped - the rig's own brow line stays in game
    from scipy import ndimage
    lab, n = ndimage.label((m * keep) > 0.3)
    sizes = ndimage.sum(np.ones_like(m), lab, range(1, n + 1))
    cy = ndimage.center_of_mass(np.ones_like(m), lab, range(1, n + 1))
    strokes = [i + 1 for i in range(n) if sizes[i] > 150]
    if len(strokes) >= 2:   # strokes: brow (topmost) + the lids (one piece, joined at the tip)
        brow = min(strokes, key=lambda i: cy[i - 1][0])
        keep[ndimage.binary_dilation(lab == brow, iterations=3)] = 0
    mask = Image.fromarray((m * keep * 255).astype(np.uint8), 'L').resize(big.size, Image.LANCZOS)
    tx, ty = HURT_EYE['target'][0] - CROP[0], HURT_EYE['target'][1] - CROP[1]
    off = (round(tx - HURT_EYE['tip'][0] * k), round(ty - HURT_EYE['tip'][1] * k))
    layer = Image.new('RGBA', (W, H), (0, 0, 0, 0)); layer.paste(big, off)
    mk = Image.new('L', (W, H), 0); mk.paste(mask, off)
    return layer, mk


EXPRESSIONS = {'Eye_Hurt': hurt_from_nai}

os.makedirs(OUT, exist_ok=True)
meta_path = os.path.join(OUT, 'sprites.json')
meta = json.load(open(meta_path, encoding='utf-8')) if os.path.exists(meta_path) else {}
tmp = tempfile.mkdtemp(prefix='face_')
jobs = []
try:
    W, H = CROP[2] - CROP[0], CROP[3] - CROP[1]
    box = eye_box()
    pivot = ((box[0] + box[2] + 1) / 2, (box[1] + box[3] + 1) / 2)
    for name, draw in EXPRESSIONS.items():
        out = os.path.join(OUT, name + '.xcf')
        meta[name] = dict(dst='Assets/Characters/%s/Face/%s.png' % (PP.CHARACTER, name), ppu=400, pivot=[round(pivot[0], 2), round(pivot[1], 2)],
                          size=[W, H], flags=[], source=os.path.relpath(HURT_SRC, ROOT).replace(os.sep, '/') if name == 'Eye_Hurt' else 'placeholder')
        if os.path.exists(out): continue
        layers = []
        def add(img, lname, **kw):
            fn = os.path.join(tmp, '%s_%d.png' % (name, len(layers))); img.save(fn); layers.append(dict(png=fn, name=lname, **kw))
        add(stack(FACE), 'REF face (ignored by Unity sync)', opacity=50, locked=True)
        add(stack(EYE), 'REF original eye (ignored by Unity sync)', opacity=35, locked=True)
        art = draw(box)
        if isinstance(art, tuple):   # (original layer, cut-out mask) -> GIMP layer mask, the layer pixels stay untouched
            lay, mk = art
            rgba = np.asarray(lay).copy(); rgba[..., 3] = (rgba[..., 3].astype(np.uint16) * np.asarray(mk) // 255).astype(np.uint8)
            add(Image.fromarray(rgba, 'RGBA'), name, main=True, mask_transfer=True)
        else:
            add(art, name, main=True)
        add(cross(W, H, pivot), 'REF pivot - centre of the normal eye (ignored by Unity sync)', locked=True)
        jobs.append(dict(out=out, w=W, h=H, layers=layers))
    json.dump(meta, open(meta_path, 'w', encoding='utf-8'), indent=1)
    if jobs:
        json.dump(jobs, open(os.path.join(tmp, 'jobs.json'), 'w', encoding='utf-8'))
        os.environ['SPRITES_TMP'] = tmp
        ok = gimp_run.run('make_sprite_files')
        print('created %d face files in ArtSource/Characters/Officer/Sprites' % sum(os.path.exists(j['out']) for j in jobs))
        sys.exit(0 if ok else 1)
    print('face files already exist; sprites.json updated')
finally:
    shutil.rmtree(tmp, ignore_errors=True)
