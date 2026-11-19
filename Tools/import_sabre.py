"""Cuts the NovelAI sabre (ArtSource/NAI_V5_rig_v3/weapons/sabre_nai45_seed44120.png, NAI v4.5 full, 1536x640, white
background, grip on the left, straight blade to the right, bow guard above and below the grip) into Assets/Character/Weapons/Saber.png.
 - removes the white background (flood fill from the borders) and the white enclosed by the knuckle bow
 - un-blends the anti-aliased rim from white
 - crops and scales so the whole sabre is LENGTH_U units long at the rig's 400 px/unit
Prints the grip pivot for WeaponSpriteImport.cs.
Usage: python Tools/import_sabre.py"""
import os
from collections import deque
import numpy as np
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, 'ArtSource', 'NAI_V5_rig_v3', 'weapons', 'sabre_nai45_seed44120.png')
DST = os.path.join(ROOT, 'Assets', 'Character', 'Weapons', 'Saber.png')
LENGTH_U = 1.8           # a real sabre is ~0.55x the wielder's height; she is ~3.3 u tall
GRIP = (210, 340)        # middle of the navy grip in the source (where the fist closes)

rgb = np.asarray(Image.open(SRC).convert('RGB')).astype(np.float32)
h, w, _ = rgb.shape
lo, hi = rgb.min(2), rgb.max(2)
white = (lo >= 236) & (hi - lo <= 14)
# the blade's bright highlight is as white as the background: protect everything between the blade's top and bottom
# outline in each column (BLADE_X0.. = blade start, searched within BLADE_Y) so neither fill can eat into it
BLADE_X0, BLADE_Y = 345, (290, 395)
ink = lo < 120
cols = []
for x in range(BLADE_X0, w):
    ys = np.nonzero(ink[BLADE_Y[0]:BLADE_Y[1], x])[0]
    if len(ys) >= 2 and ys[-1] - ys[0] > 3: cols.append((x, BLADE_Y[0] + ys[0], BLADE_Y[0] + ys[-1]))
# the glare washes the outline out mid-blade: fit the two edges as lines (robust: median of the per-column values
# around the fit) and protect between them up to the tip
cx = np.array([c[0] for c in cols], float)
top = np.polyfit(cx, [c[1] for c in cols], 1); bot = np.polyfit(cx, [c[2] for c in cols], 1)
tip_x = int(cx.max())
for x in range(BLADE_X0, tip_x + 1):
    y0, y1 = np.polyval(top, x), np.polyval(bot, x)
    if y1 - y0 < 1: continue
    white[int(np.floor(y0)):int(np.ceil(y1)) + 1, x] = False

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
# white enclosed by the knuckle bow / guard: large flat near-white patches
seen = bg.copy()
for sy, sx in zip(*np.nonzero(white & ~bg)):
    if seen[sy, sx] or sx >= BLADE_X0: continue   # enclosed white only inside the hilt
    pts = []; q = deque([(sy, sx)]); seen[sy, sx] = True
    while q:
        y, x = q.popleft(); pts.append((y, x))
        for ny, nx in ((y + 1, x), (y - 1, x), (y, x + 1), (y, x - 1)):
            if 0 <= ny < h and 0 <= nx < w and white[ny, nx] and not seen[ny, nx]:
                seen[ny, nx] = True; q.append((ny, nx))
    if len(pts) >= 40 and np.mean([lo[p] for p in pts]) >= 244:
        ys, xs = zip(*pts); bg[ys, xs] = True

# blade columns: nothing outside the fitted blade band (drops the glare halo); the tip tapers to a point over the
# last TIP px so it is not cut square
TIP = 60
for x in range(BLADE_X0, w):
    if x > tip_x: bg[:, x] = True; continue
    y0, y1 = np.polyval(top, x), np.polyval(bot, x)
    if x > tip_x - TIP:
        mid, k = (y0 + y1) / 2, (tip_x - x) / TIP
        y0, y1 = mid - (mid - y0) * k, mid + (y1 - mid) * k
    bg[:max(0, int(np.floor(y0)) - 1), x] = True
    bg[int(np.ceil(y1)) + 2:, x] = True
alpha = np.where(bg, 0.0, 1.0)
rim = bg.copy()
for _ in range(2):
    rim = rim | np.roll(rim, 1, 0) | np.roll(rim, -1, 0) | np.roll(rim, 1, 1) | np.roll(rim, -1, 1)
rim &= ~bg
a = np.clip((255.0 - lo[rim]) / (255.0 - 60.0), 0.0, 1.0)
keep = np.maximum(a, 1e-3)[:, None]
rgb[rim] = np.clip((rgb[rim] - (1.0 - keep) * 255.0) / keep, 0, 255)
alpha[rim] = a

ys, xs = np.nonzero(alpha > 0.05)
x0, x1, y0, y1 = xs.min() - 4, xs.max() + 5, ys.min() - 4, ys.max() + 5
rgba = np.dstack([rgb, alpha * 255.0])[y0:y1, x0:x1]
im = Image.fromarray((rgba + 0.5).astype(np.uint8), 'RGBA')
s = LENGTH_U * 400.0 / (x1 - x0)
im = im.resize((round(im.width * s), round(im.height * s)), Image.LANCZOS)
im.save(DST)
px, py = (GRIP[0] - x0) * s, (GRIP[1] - y0) * s
print('Saber.png %dx%d, pivot px (%.1f, %.1f) -> normalized (%.4f, %.4f) [y from bottom]'
      % (im.width, im.height, px, py, px / im.width, 1 - py / im.height))
