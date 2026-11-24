"""Cuts the NovelAI glove poses (ArtSource/Characters/Officer/Source/hands, NAI v4.5 full, 1024x1024 on white) into
Assets/Characters/Officer/Hands/Hand_<Name>.png, pivot at the WRIST, scaled to the rig (400 px/unit, the drawn fist is 0.27 u).
Per pose: crop box, wrist point, tip point (end of the hand: knuckles for a fist, middle fingertip for an open hand) and
the hand length in units. The glove colour is matched to the rig's drawn glove (mean ~ (37, 38, 49)).
Skin (arm above the glove) and anything outside the crop are removed.
Prints for each pose: sprite size, PPU-free size, pivot and the hand angle in the sprite (deg, +X = 0) for HandPoses.cs.
Usage: python Tools/import_hands.py"""
import math, os
from collections import deque
import numpy as np
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, 'ArtSource', 'Characters', 'Officer', 'Source', 'hands')
DST = os.path.join(ROOT, 'Assets', 'Characters', 'Officer', 'Hands')
PPU = 400.0
RIG_GLOVE = np.array([37.0, 38.0, 49.0])

POSES = {
    # name: (file, crop (x0, y0, x1, y1), wrist, tip, hand length u, extra point name -> source px)
    'GripInline': ('grip_inline_nai45_seed12092.png', (232, 270, 588, 660), (238, 468), (578, 452), 0.25,
                   {'grip': (470, 455)}),    # the sword runs through the fist along wrist->tip
    # NAI V5 + wiki preset nai-v5-yamada9000-fis-mix: back of the right hand, thumb on top, the grip crossing the fist at
    # ~77 deg to the wrist->knuckle axis (how a real fist holds a hilt). The generated grip stubs are erased (ERASE) so
    # the game's sabre, drawn under the hand, shows instead. The glove cuff stays: the sleeve covers it.
    'Grip': ('grip_back_naiv5_preset_seed30102.png', (130, 0, 900, 1024), (290, 500), (840, 600), 0.27,
             {'grip': (617, 540), 'gripAxisTop': (790, 130)}),
    'Open': ('open_nai45_seed12101.png', (120, 60, 910, 1012), (488, 120), (452, 985), 0.33, {}),
    'Spread': ('spread_nai45_seed12102.png', (95, 30, 995, 1000), (285, 905), (968, 385), 0.36, {}),
}


ERASE = {   # polygons (source px) removed after the cut
    'Grip': [[(560, 392), (680, 0), (940, 0), (940, 60), (800, 402)], [(380, 792), (600, 792), (650, 1024), (350, 1024)]],
}


def cut(rgb, crop):
    h, w, _ = rgb.shape
    lo, hi = rgb.min(2), rgb.max(2)
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    white = (lo >= 228) & (hi - lo <= 20)
    skin = (r > b + 25) & (r > 120)                       # bare arm above the glove, orange tints
    keep = np.zeros((h, w), bool)
    x0, y0, x1, y1 = crop
    keep[y0:y1, x0:x1] = True
    bg = ~keep | skin
    q = deque([(y, x) for y in range(h) for x in (0, w - 1)] + [(y, x) for x in range(w) for y in (0, h - 1)])
    seen = np.zeros((h, w), bool)
    while q:
        y, x = q.popleft()
        if seen[y, x]: continue
        seen[y, x] = True
        if not (white[y, x] or bg[y, x]): continue
        bg[y, x] = True
        for ny, nx in ((y + 1, x), (y - 1, x), (y, x + 1), (y, x - 1)):
            if 0 <= ny < h and 0 <= nx < w and not seen[ny, nx]: q.append((ny, nx))
    alpha = np.where(bg, 0.0, 1.0)
    rim = bg.copy()
    for _ in range(2):
        rim = rim | np.roll(rim, 1, 0) | np.roll(rim, -1, 0) | np.roll(rim, 1, 1) | np.roll(rim, -1, 1)
    rim &= ~bg
    a = np.clip((255.0 - lo[rim]) / (255.0 - 60.0), 0.0, 1.0)
    keepa = np.maximum(a, 1e-3)[:, None]
    rgb = rgb.copy()
    rgb[rim] = np.clip((rgb[rim] - (1.0 - keepa) * 255.0) / keepa, 0, 255)
    alpha[rim] = a
    return rgb, alpha


os.makedirs(DST, exist_ok=True)
for name, (f, crop, wrist, tip, length_u, extra) in POSES.items():
    rgb = np.asarray(Image.open(os.path.join(SRC, f)).convert('RGB')).astype(np.float32)
    rgb, alpha = cut(rgb, crop)
    if name in ERASE:
        from PIL import ImageDraw
        m = Image.new('L', (alpha.shape[1], alpha.shape[0]), 0); d = ImageDraw.Draw(m)
        for poly in ERASE[name]: d.polygon(poly, fill=255)
        alpha = np.where(np.asarray(m) > 0, 0.0, alpha)
    glove = (alpha > 0.9) & (rgb.max(2) < 150)
    if glove.any():   # match the glove colour to the rig (mean shift, keeps the shading)
        shift = RIG_GLOVE - rgb[glove].mean(0)
        rgb = np.clip(rgb + shift * np.clip((160 - rgb.max(2, keepdims=True)) / 160, 0, 1), 0, 255)
    ys, xs = np.nonzero(alpha > 0.05)
    bx0, bx1, by0, by1 = xs.min() - 2, xs.max() + 3, ys.min() - 2, ys.max() + 3
    img = np.dstack([rgb, alpha * 255])[by0:by1, bx0:bx1]
    src_len = math.hypot(tip[0] - wrist[0], tip[1] - wrist[1])
    s = length_u * PPU / src_len
    im = Image.fromarray((img + 0.5).astype(np.uint8), 'RGBA')
    im = im.resize((max(1, round(im.width * s)), max(1, round(im.height * s))), Image.LANCZOS)
    im.save(os.path.join(DST, 'Hand_%s.png' % name))
    px, py = (wrist[0] - bx0) * s, (wrist[1] - by0) * s
    ang = math.degrees(math.atan2(-(tip[1] - wrist[1]), tip[0] - wrist[0]))
    ex = ', '.join('%s (%.3f, %.3f) u from the wrist' % (k, (v[0] - wrist[0]) * s / PPU, -(v[1] - wrist[1]) * s / PPU) for k, v in extra.items())
    print('Hand_%s.png %dx%d pivot (%.4f, %.4f) handAngle %.1f %s' % (name, im.width, im.height, px / im.width, 1 - py / im.height, ang, ex))
