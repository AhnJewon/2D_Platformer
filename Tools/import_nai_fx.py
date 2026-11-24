"""Imports the NovelAI-made effect and target art (NAI v4.5 full) into Assets/Weapons:
 - Slash.png from ArtSource/Weapons/Source/slash_nai45_seed5150.png: a glowing crescent on BLACK. Alpha is taken
   from brightness and the colour is un-premultiplied, so it reads the same over any background. The crescent is
   thickest at +X (the aim direction in the game); the outer radius is ~372 px around (545, 532).
 - Dummy.png from dummy_nai45_seed8843.png: straw training dummy on white, background flood-filled out.
Import settings (PPU / pivot): Assets/Editor/WeaponSpriteImport.cs - printed below.
Usage: python Tools/import_nai_fx.py"""
import os
from collections import deque
import numpy as np
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, 'ArtSource', 'Weapons', 'Source')
DST = os.path.join(ROOT, 'Assets', 'Weapons')

# ---- slash ----
rgb = np.asarray(Image.open(os.path.join(SRC, 'slash_nai45_seed5150.png')).convert('RGB')).astype(np.float32) / 255.0
cx, cy, R = 545, 532, 372
half = 400                                      # crop a square around the centre (keeps the glow)
crop = rgb[cy - half:cy + half, cx - half:cx + half]
a = crop.max(2)
a = np.clip((a - 0.06) / 0.94, 0, 1)            # drop the near-black noise floor
# the moon-shaped crescent wraps ~300 deg; fade its thin tails out past +-100 deg so nothing trails behind her
yy, xx = np.mgrid[0:2 * half, 0:2 * half]
ang = np.degrees(np.abs(np.arctan2(-(yy - half + 0.5), xx - half + 0.5)))
a = a * np.clip((150.0 - ang) / 50.0, 0, 1)
col = np.clip(crop / np.maximum(crop.max(2), 1e-3)[..., None], 0, 1)   # un-premultiply by brightness
col = col * 0.75 + 0.25                         # lift toward white so it reads over the dark coat too
out = np.dstack([np.clip(col, 0, 1), a])
# thin the band: the NAI crescent is ~40% of the radius thick, which hid the character in game. Remap radially so the
# band (outer radius R) is THIN x as thick: output radius r' samples the source at R - (R - r') / THIN.
from scipy.ndimage import map_coordinates
THIN = 0.5
yy2, xx2 = np.mgrid[0:2 * half, 0:2 * half].astype(np.float32)
dx, dy = xx2 - half + 0.5, yy2 - half + 0.5
rr = np.sqrt(dx * dx + dy * dy) + 1e-6
src_r = R - (R - rr) / THIN
src_r = np.where(rr > R, rr, src_r)                 # keep the outer glow as it is
src_r = np.where(src_r < 0, 1e6, src_r)             # nothing inside the remapped band (no wrap-around)
sx, sy = half - 0.5 + dx / rr * src_r, half - 0.5 + dy / rr * src_r
out = np.stack([map_coordinates(out[..., k], [sy, sx], order=1, mode='constant', cval=0.0) for k in range(4)], -1)
im = Image.fromarray((out * 255 + 0.5).astype(np.uint8), 'RGBA').resize((512, 512), Image.LANCZOS)
im.save(os.path.join(DST, 'Slash.png'))
r_px = R * 512 / (2 * half)
print('Slash.png 512x512: outer radius %.0f px -> PPU %.0f for 1.25 u, pivot centre' % (r_px, r_px / 1.25))

# ---- dummy ----
rgb = np.asarray(Image.open(os.path.join(SRC, 'dummy_nai45_seed8843.png')).convert('RGB')).astype(np.float32)
h, w, _ = rgb.shape
lo, hi = rgb.min(2), rgb.max(2)
white = (lo >= 232) & (hi - lo <= 16)
bg = np.zeros((h, w), bool)
q = deque([(y, x) for y in range(h) for x in (0, w - 1)] + [(y, x) for x in range(w) for y in (0, h - 1)])
while q:
    y, x = q.popleft()
    if bg[y, x] or not white[y, x]: continue
    bg[y, x] = True
    for ny, nx in ((y + 1, x), (y - 1, x), (y, x + 1), (y, x - 1)):
        if 0 <= ny < h and 0 <= nx < w: q.append((ny, nx))
# enclosed background (between the crossed arms / ropes and the body)
seen = bg.copy()
for sy, sx in zip(*np.nonzero(white & ~bg)):
    if seen[sy, sx]: continue
    pts = []; q = deque([(sy, sx)]); seen[sy, sx] = True
    while q:
        y, x = q.popleft(); pts.append((y, x))
        for ny, nx in ((y + 1, x), (y - 1, x), (y, x + 1), (y, x - 1)):
            if 0 <= ny < h and 0 <= nx < w and white[ny, nx] and not seen[ny, nx]:
                seen[ny, nx] = True; q.append((ny, nx))
    if len(pts) >= 150 and np.mean([lo[p] for p in pts]) >= 243:
        ys, xs = zip(*pts); bg[ys, xs] = True
alpha = np.where(bg, 0.0, 1.0)
rim = bg.copy()
for _ in range(2):
    rim = rim | np.roll(rim, 1, 0) | np.roll(rim, -1, 0) | np.roll(rim, 1, 1) | np.roll(rim, -1, 1)
rim &= ~bg
ra = np.clip((255.0 - lo[rim]) / (255.0 - 70.0), 0.0, 1.0)
keep = np.maximum(ra, 1e-3)[:, None]
rgb[rim] = np.clip((rgb[rim] - (1.0 - keep) * 255.0) / keep, 0, 255)
alpha[rim] = ra
ys, xs = np.nonzero(alpha > 0.05)
x0, x1, y0, y1 = xs.min() - 3, xs.max() + 4, ys.min() - 3, ys.max() + 4
img = np.dstack([rgb, alpha * 255.0])[y0:y1, x0:x1]
im = Image.fromarray((img + 0.5).astype(np.uint8), 'RGBA')
im = im.resize((im.width // 2, im.height // 2), Image.LANCZOS)
im.save(os.path.join(DST, 'Dummy.png'))
H_U = 2.3
print('Dummy.png %dx%d: PPU %.0f for %.1f u tall, pivot (0.5, %.4f)' % (im.width, im.height, im.height / H_U, H_U, 3 / im.height))
