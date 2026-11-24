"""Create <set>/Sprites/<name>.xcf (one GIMP file per loose sprite) and that set's sprites.json; sets are
ArtSource/Characters/<Name>/Sprites (hands), ArtSource/Weapons/Sprites, ArtSource/Props/Sprites. Existing XCF files are left untouched.

The sprite layer is the UNTOUCHED NovelAI original (full source resolution, every pixel kept) with the cut-out as a
GIMP layer mask (Tools/sprite_masks.py: background removal, erased hilt stubs, tail fade). Fix a wrong cut by painting
the mask (white = keep, black = remove); repaint the art itself on the layer. No colour or shape edits are baked in.
Locked REF layers (ignored by the sync) give context, drawn at the same scale:
  REF pivot     cross at the pivot (wrist / grip / centre / foot of the base) - the canvas fixes it, keep the art there
  REF sleeve    hands: the rig's forearm, its cuff ending at the wrist, along the arm direction
  REF sabre     Hand_Grip: the game sabre where the game puts it (hilt through the fist, drawn under the hand)
sprites.json: destination PNG, px/unit (source pixels), pivot (canvas px), canvas size and export flags; read by
Tools/update_sprites.py and Assets/Editor/WeaponSpriteImport.cs.
Usage: python Tools/make_sprite_files.py"""
import json, math, os, shutil, sys, tempfile
import numpy as np
from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, 'Tools'))
import gimp_run, sprite_masks, project_paths as PP

NAI = sprite_masks.NAI
PARTS = os.path.join(PP.source_dir(), 'parts_cropped_filled')
RIG_PPU = 400.0

# name: (Sprites folder, Unity destination)
_C = 'Assets/Characters/%s/' % PP.CHARACTER
DST = {'Hand_Grip': (PP.sprites_dir(), _C + 'Hands/Hand_Grip.png'), 'Hand_Open': (PP.sprites_dir(), _C + 'Hands/Hand_Open.png'),
       'Hand_Spread': (PP.sprites_dir(), _C + 'Hands/Hand_Spread.png'),
       'Saber': (os.path.join(PP.WEAPONS, 'Sprites'), 'Assets/Weapons/Saber.png'), 'Slash': (os.path.join(PP.WEAPONS, 'Sprites'), 'Assets/Weapons/Slash.png'),
       'Pistol': (os.path.join(PP.WEAPONS, 'Sprites'), 'Assets/Weapons/Pistol.png'), 'Bolt': (os.path.join(PP.WEAPONS, 'Sprites'), 'Assets/Weapons/Bolt.png'),
       'MuzzleFlash': (os.path.join(PP.WEAPONS, 'Sprites'), 'Assets/Weapons/MuzzleFlash.png'),
       'Dummy': (os.path.join(PP.PROPS, 'Sprites'), 'Assets/Props/Dummy.png')}
HAND_CTX = {   # wrist->tip axis in the drawing (deg), forearm part for the REF sleeve, sabre for Hand_Grip
    'Hand_Grip': dict(axis=-10.3, forearm='12_arm_near_forearm.png', sabre=dict(offset=77.4, grip=(0.158, -0.019))),
    'Hand_Open': dict(axis=-92.4, forearm='10b_arm_far_forearm.png'),
    'Hand_Spread': dict(axis=37.3, forearm='10b_arm_far_forearm.png'),
}


def cross(w, h, p, r):
    im = Image.new('RGBA', (w, h), (0, 0, 0, 0)); d = ImageDraw.Draw(im); x, y = p
    lw = max(2, int(r / 6))
    d.line([(x - r, y), (x + r, y)], fill=(255, 40, 40, 255), width=lw); d.line([(x, y - r), (x, y + r)], fill=(255, 40, 40, 255), width=lw)
    d.ellipse([x - r / 3, y - r / 3, x + r / 3, y + r / 3], outline=(255, 40, 40, 255), width=lw)
    return im


def paste_rotated(canvas, img, anchor_src, anchor_dst, deg):
    w, h = img.size
    big = Image.new('RGBA', (w * 3, h * 3), (0, 0, 0, 0)); big.paste(img, (w, h))
    ax, ay = anchor_src[0] + w, anchor_src[1] + h
    rot = big.rotate(deg, resample=Image.BICUBIC, center=(ax, ay))
    canvas.alpha_composite(rot, (int(round(anchor_dst[0] - ax)), int(round(anchor_dst[1] - ay))))


def scaled(img, k):
    return img.resize((max(1, round(img.width * k)), max(1, round(img.height * k))), Image.LANCZOS)


metas = {}
def meta_of(d):
    if d not in metas:
        mp = os.path.join(d, 'sprites.json')
        metas[d] = json.load(open(mp, encoding='utf-8')) if os.path.exists(mp) else {}
    return metas[d]
tmp = tempfile.mkdtemp(prefix='sprites_')
jobs = []
try:
    masks = sprite_masks.all_masks()
    for name, m in masks.items():
        OUT, dst = DST[name]
        os.makedirs(OUT, exist_ok=True)
        meta = meta_of(OUT)
        out = os.path.join(OUT, name + '.xcf')
        if os.path.exists(out) and name in meta: continue      # the XCF is the source: keep it
        src = Image.open(os.path.join(NAI, m['src'])).convert('RGB')
        W, H = src.size; P = tuple(float(v) for v in m['pivot']); ppu = float(m['ppu']); k = ppu / RIG_PPU
        layers = []
        def add(img, lname, **kw):
            fn = os.path.join(tmp, '%s_%d.png' % (name, len(layers))); img.save(fn); layers.append(dict(png=fn, name=lname, **kw))
        if name in HAND_CTX:
            ex = HAND_CTX[name]
            fa = scaled(Image.open(os.path.join(PARTS, ex['forearm'])).convert('RGBA'), k)
            ctx = Image.new('RGBA', (W, H), (0, 0, 0, 0))
            paste_rotated(ctx, fa, (fa.width / 2, fa.height - 6 * k), P, ex['axis'] + 90)
            add(ctx, 'REF sleeve (rig forearm, ignored by Unity sync)', opacity=45, locked=True)
            if 'sabre' in ex:
                sm = masks['Saber']; s_src = Image.open(os.path.join(NAI, sm['src'])).convert('RGB')
                sab = np.dstack([np.asarray(s_src), (sm['alpha'] * 255).astype(np.uint8)])
                sab = scaled(Image.fromarray(sab, 'RGBA'), ppu / sm['ppu'])
                spv = (sm['pivot'][0] * ppu / sm['ppu'], sm['pivot'][1] * ppu / sm['ppu'])
                a = math.radians(ex['axis']); gx, gy = ex['sabre']['grip']
                gp = (P[0] + (gx * math.cos(a) - gy * math.sin(a)) * ppu, P[1] - (gx * math.sin(a) + gy * math.cos(a)) * ppu)
                big = Image.new('RGBA', (W, H), (0, 0, 0, 0))
                paste_rotated(big, sab, spv, gp, ex['axis'] + ex['sabre']['offset'])
                add(big, 'REF sabre in the grip (drawn UNDER the hand in game, ignored by Unity sync)', opacity=60, locked=True)
        # main: original RGB untouched, cut-out in the alpha -> GIMP moves the alpha into a layer mask
        rgba = np.dstack([np.asarray(src), (np.clip(m['alpha'], 0, 1) * 255 + 0.5).astype(np.uint8)])
        add(Image.fromarray(rgba, 'RGBA'), name, main=True, mask_transfer=True)
        if name == 'Dummy':
            g = Image.new('RGBA', (W, H), (0, 0, 0, 0)); ImageDraw.Draw(g).line([(0, P[1]), (W, P[1])], fill=(80, 160, 80, 255), width=3)
            add(g, 'REF ground line (ignored by Unity sync)', opacity=80, locked=True)
        add(cross(W, H, P, max(14, 0.035 * ppu)), 'REF pivot - keep the wrist/grip here (ignored by Unity sync)', locked=True)
        meta[name] = dict(dst=dst, ppu=round(ppu, 3), pivot=[round(P[0], 2), round(P[1], 2)],
                          size=[W, H], flags=m['flags'], source='ArtSource/' + m['src'])
        jobs.append(dict(out=out, w=W, h=H, layers=layers))
    for d, mt in metas.items(): json.dump(mt, open(os.path.join(d, 'sprites.json'), 'w', encoding='utf-8'), indent=1)
    if jobs:
        json.dump(jobs, open(os.path.join(tmp, 'jobs.json'), 'w', encoding='utf-8'))
        os.environ['SPRITES_TMP'] = tmp
        ok = gimp_run.run('make_sprite_files')
        made = [j['out'] for j in jobs if os.path.exists(j['out'])]
        print('created %d sprite files' % len(made))
        sys.exit(0 if ok and len(made) == len(jobs) else 1)
    print('all sprite files already exist; sprites.json kept')
finally:
    shutil.rmtree(tmp, ignore_errors=True)
