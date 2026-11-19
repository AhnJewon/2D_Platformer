"""Cut-out MASKS (alpha only, at the NovelAI source resolution) for the loose sprites. Used by Tools/make_sprite_files.py
to build ArtSource/Sprites/<name>.xcf = the untouched NAI original + this mask as a GIMP layer mask.
BACKGROUND REMOVAL ONLY: nothing that belongs to the drawing is cut (no crop boxes, no erased hilt stubs, no tail fade,
no taper). The only extra rule protects the sabre blade, whose glare is as white as the background, from being eaten.

Each function returns dict(src, alpha (H, W float 0..1), pivot (x, y source px), ppu (source px per unit), flags).
flags:
  defringe_white - the source was on white: Tools/update_sprites.py un-blends half-transparent edge pixels from white
  from_black     - glow on black: update_sprites.py turns brightness into alpha (and un-premultiplies) before the mask
"""
import math, os
from collections import deque
import numpy as np
from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
NAI = os.path.join(ROOT, 'ArtSource', 'NAI_V5_rig_v3')


def load(rel):
    return np.asarray(Image.open(os.path.join(NAI, rel)).convert('RGB')).astype(np.float32)


def flood_bg(white, seed_mask=None):
    h, w = white.shape
    bg = np.zeros((h, w), bool)
    q = deque([(y, x) for y in range(h) for x in (0, w - 1)] + [(y, x) for x in range(w) for y in (0, h - 1)])
    while q:
        y, x = q.popleft()
        if bg[y, x] or not white[y, x]: continue
        bg[y, x] = True
        if y > 0: q.append((y - 1, x))
        if y < h - 1: q.append((y + 1, x))
        if x > 0: q.append((y, x - 1))
        if x < w - 1: q.append((y, x + 1))
    return bg


def enclosed_white(white, bg, min_size, min_lo, lo, xmax=None):
    h, w = white.shape
    seen = bg.copy()
    for sy, sx in zip(*np.nonzero(white & ~bg)):
        if seen[sy, sx] or (xmax is not None and sx >= xmax): continue
        pts = []; q = deque([(sy, sx)]); seen[sy, sx] = True
        while q:
            y, x = q.popleft(); pts.append((y, x))
            for ny, nx in ((y + 1, x), (y - 1, x), (y, x + 1), (y, x - 1)):
                if 0 <= ny < h and 0 <= nx < w and white[ny, nx] and not seen[ny, nx]:
                    seen[ny, nx] = True; q.append((ny, nx))
        if len(pts) >= min_size and np.mean([lo[p] for p in pts]) >= min_lo:
            ys, xs = zip(*pts); bg[ys, xs] = True
    return bg


def soft_edge(bg, lo, ink=60.0):
    """alpha 0 on the background, 1 inside, and a 2-px rim graded by darkness (anti-aliased edge)"""
    alpha = np.where(bg, 0.0, 1.0)
    rim = bg.copy()
    for _ in range(2):
        rim = rim | np.roll(rim, 1, 0) | np.roll(rim, -1, 0) | np.roll(rim, 1, 1) | np.roll(rim, -1, 1)
    rim &= ~bg
    alpha[rim] = np.clip((255.0 - lo[rim]) / (255.0 - ink), 0.0, 1.0)
    return alpha


def sabre():
    rel = 'weapons/sabre_nai45_seed44120.png'
    rgb = load(rel); h, w, _ = rgb.shape
    lo, hi = rgb.min(2), rgb.max(2)
    white = (lo >= 236) & (hi - lo <= 14)
    # the blade's glare is as white as the background: keep everything between the fitted blade edges
    BLADE_X0, BY = 345, (290, 395)
    ink = lo < 120
    cols = []
    for x in range(BLADE_X0, w):
        ys = np.nonzero(ink[BY[0]:BY[1], x])[0]
        if len(ys) >= 2 and ys[-1] - ys[0] > 3: cols.append((x, BY[0] + ys[0], BY[0] + ys[-1]))
    cx = np.array([c[0] for c in cols], float)
    top = np.polyfit(cx, [c[1] for c in cols], 1); bot = np.polyfit(cx, [c[2] for c in cols], 1)
    tip_x = int(cx.max())
    for x in range(BLADE_X0, tip_x + 1):
        y0, y1 = np.polyval(top, x), np.polyval(bot, x)
        if y1 - y0 >= 1: white[int(np.floor(y0)):int(np.ceil(y1)) + 1, x] = False
    bg = flood_bg(white)
    bg = enclosed_white(white, bg, 40, 244, lo, xmax=BLADE_X0)
    alpha = soft_edge(bg, lo)
    xs = np.nonzero(alpha.max(0) > 0.05)[0]
    length_px = xs.max() - xs.min() + 9
    return dict(src=rel, alpha=alpha, pivot=(210, 340), ppu=length_px / 1.8, flags=['defringe_white'])


def dummy():
    rel = 'weapons/dummy_nai45_seed8843.png'
    rgb = load(rel)
    lo, hi = rgb.min(2), rgb.max(2)
    white = (lo >= 232) & (hi - lo <= 16)
    bg = flood_bg(white)
    bg = enclosed_white(white, bg, 150, 243, lo)
    alpha = soft_edge(bg, lo, ink=70.0)
    ys, xs = np.nonzero(alpha > 0.05)
    return dict(src=rel, alpha=alpha, pivot=((xs.min() + xs.max()) / 2, ys.max()), ppu=(ys.max() - ys.min() + 6) / 2.3,
                flags=['defringe_white'])


def slash():
    rel = 'weapons/slash_nai45_seed5150.png'
    rgb = load(rel); h, w, _ = rgb.shape
    cx, cy, R = 545, 532, 372
    alpha = np.ones((h, w), np.float32)   # the black background is removed on export (from_black); nothing cut here
    return dict(src=rel, alpha=alpha, pivot=(cx, cy), ppu=R / 1.25, flags=['from_black'])


HANDS = {
    # name: (file, crop (unused: nothing is cropped), wrist, tip, hand length u, erase (unused))
    'Hand_Grip': ('hands/grip_back_naiv5_preset_seed30102.png', (130, 0, 900, 1024), (290, 500), (840, 600), 0.27,
                  [[(560, 392), (680, 0), (940, 0), (940, 60), (800, 402)], [(380, 792), (600, 792), (650, 1024), (350, 1024)]]),
    'Hand_Open': ('hands/open_nai45_seed12101.png', (120, 60, 910, 1012), (488, 120), (452, 985), 0.33, []),
    'Hand_Spread': ('hands/spread_nai45_seed12102.png', (95, 30, 995, 1000), (285, 905), (968, 385), 0.36, []),
}


def hand(name):
    rel, crop, wrist, tip, length_u, erase = HANDS[name]
    rgb = load(rel); h, w, _ = rgb.shape
    lo, hi = rgb.min(2), rgb.max(2)
    white = (lo >= 228) & (hi - lo <= 20)
    bg = flood_bg(white)                         # background only: arm skin, cuffs and hilt pieces stay
    bg = enclosed_white(white, bg, 150, 243, lo) # white background enclosed between fingers / the hilt
    alpha = soft_edge(bg, lo)
    src_len = math.hypot(tip[0] - wrist[0], tip[1] - wrist[1])
    return dict(src=rel, alpha=alpha, pivot=wrist, ppu=src_len / length_u, flags=['defringe_white'])


def pistol():
    # NAI V5 + wiki preset, seed 40101: sci-fi service pistol, muzzle right. Pivot = middle of the grip (the fist holds
    # it there); ~0.55 u long. Grip axis ~69 deg up from the bore; muzzle at (1127, 350).
    rel = 'weapons/pistol_naiv5_preset_seed40101.png'
    rgb = load(rel)
    lo, hi = rgb.min(2), rgb.max(2)
    white = (lo >= 232) & (hi - lo <= 16)
    bg = flood_bg(white)
    bg = enclosed_white(white, bg, 150, 243, lo)   # the hole of the trigger guard
    alpha = soft_edge(bg, lo)
    return dict(src=rel, alpha=alpha, pivot=(250, 560), ppu=(1127 - 79) / 0.55, flags=['defringe_white'])


def bolt():
    # NAI V5 seed 40201: cyan energy bolt on black, head at the right. Pivot at the head; 0.7 u long.
    rel = 'weapons/bolt_naiv5_seed40201.png'
    h, w = load(rel).shape[:2]
    return dict(src=rel, alpha=np.ones((h, w), np.float32), pivot=(1030, 405), ppu=(1031 - 80) / 0.7, flags=['from_black'])


def muzzle_flash():
    # NAI V5 seed 40301: cyan starburst on black, firing right. Pivot at its origin (left middle); ~0.5 u.
    rel = 'weapons/muzzleflash_naiv5_seed40301.png'
    h, w = load(rel).shape[:2]
    return dict(src=rel, alpha=np.ones((h, w), np.float32), pivot=(165, 520), ppu=(990 - 97) / 0.5, flags=['from_black'])


def all_masks():
    out = {n: hand(n) for n in HANDS}
    out['Saber'] = sabre(); out['Slash'] = slash(); out['Dummy'] = dummy()
    out['Pistol'] = pistol(); out['Bolt'] = bolt(); out['MuzzleFlash'] = muzzle_flash()
    return out
