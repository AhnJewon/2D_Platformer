"""Make the slide sprite in Assets/Characters/Officer/Slide from the pose the user chose: sitting slide, far knee up, hand on
the cap. Default source: the original baseball_slide_sketch_pose_rgba.png (1376x768, transparent, ~0.864x the rig's
pixel scale -> 346 px/unit). Optional --sheet: the half-size 2x2 sheet slide_kneeup_4frames.webp (4 frames).
 - cuts the 4 frames (688x384 each), removes the flat light background (flood fill from the borders, so the
   outlined white hair is kept), and recolours the bright cyan coat lining to the rig's navy lining.
Import scale/pivot: Assets/Editor/SlideSpriteImport.cs.
Usage: python Tools/import_slide_frames.py"""
import colorsys, os
from collections import deque
import numpy as np
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, 'ArtSource', 'Characters', 'Officer', 'Source', 'slide_frames', 'slide_kneeup_4frames.webp')
ORIGINAL = os.path.join(ROOT, 'ArtSource', 'Characters', 'Officer', 'Source', 'slide_frames', 'baseball_slide_sketch_pose_rgba.png')
DST = os.path.join(ROOT, 'Assets', 'Characters', 'Officer', 'Slide')
TARGET_HUE = 212 / 360.0   # rig lining (#194c87)
VALUE_SCALE = 135 / 207.0  # rig lining median value / sheet lining median value
TOL = 18                   # background tolerance (max channel difference)


def cut_background(rgb):
    h, w, _ = rgb.shape
    bg = np.median(np.concatenate([rgb[0], rgb[-1], rgb[:, 0], rgb[:, -1]]), axis=0)
    near = np.abs(rgb.astype(int) - bg).max(2) <= TOL
    out = np.zeros((h, w), bool)
    q = deque([(y, x) for y in range(h) for x in (0, w - 1)] + [(y, x) for x in range(w) for y in (0, h - 1)])
    while q:
        y, x = q.popleft()
        if out[y, x] or not near[y, x]: continue
        out[y, x] = True
        if y > 0: q.append((y - 1, x))
        if y < h - 1: q.append((y + 1, x))
        if x > 0: q.append((y, x - 1))
        if x < w - 1: q.append((y, x + 1))
    # background enclosed by outlines (between hair strands, under the raised knee, arm/face gap): any remaining
    # near-background patch of 80+ px is a hole, not a highlight (hair highlights are thin and not this flat colour)
    hole = np.abs(rgb.astype(int) - bg).max(2) <= 10
    seen = out.copy()
    for sy in range(h):
        for sx in range(w):
            if hole[sy, sx] and not seen[sy, sx]:
                q = deque([(sy, sx)]); seen[sy, sx] = True; pts = []
                while q:
                    y, x = q.popleft(); pts.append((y, x))
                    for ny, nx in ((y + 1, x), (y - 1, x), (y, x + 1), (y, x - 1)):
                        if 0 <= ny < h and 0 <= nx < w and hole[ny, nx] and not seen[ny, nx]:
                            seen[ny, nx] = True; q.append((ny, nx))
                if len(pts) >= 80:
                    for y, x in pts: out[y, x] = True
    alpha = np.where(out, 0, 255).astype(np.uint8)
    # soften the 1-px light fringe left by anti-aliasing against the old background
    edge = (~out) & (np.roll(out, 1, 0) | np.roll(out, -1, 0) | np.roll(out, 1, 1) | np.roll(out, -1, 1))
    light = np.abs(rgb.astype(int) - bg).max(2) < 60
    alpha[edge & light] = 110
    return alpha


def clear_enclosed_white(rgba, min_size=250):
    """The original PNG was cut out from the outside only: white background enclosed by the outlines (behind the
    raised arm, under the knee, between hair and coat, gaps between hair strands) is still opaque. Any flat white
    patch of min_size+ px is such a hole (hair highlights are smaller and slightly blue/grey). The 2-px anti-aliased
    rim around each hole is un-blended from white so no light fringe is left."""
    out = rgba.astype(np.float32).copy()
    rgb = out[..., :3]
    lo, hi = rgb.min(2), rgb.max(2)
    white = (out[..., 3] > 0) & (lo >= 232) & (hi - lo <= 8)
    hole = np.zeros(white.shape, bool)
    seen = np.zeros(white.shape, bool)
    h, w = white.shape
    for sy, sx in zip(*np.nonzero(white)):
        if seen[sy, sx]: continue
        q = deque([(sy, sx)]); seen[sy, sx] = True; pts = []
        while q:
            y, x = q.popleft(); pts.append((y, x))
            for ny, nx in ((y + 1, x), (y - 1, x), (y, x + 1), (y, x - 1)):
                if 0 <= ny < h and 0 <= nx < w and white[ny, nx] and not seen[ny, nx]:
                    seen[ny, nx] = True; q.append((ny, nx))
        if len(pts) >= min_size and np.mean([lo[p] for p in pts]) >= 250:
            ys, xs = zip(*pts); hole[ys, xs] = True
    out[hole, 3] = 0
    rim = out[..., 3] == 0   # the outer cut-out edge has the same light fringe, so clean it too
    rim = rim.copy()
    for _ in range(2):
        rim = rim | np.roll(rim, 1, 0) | np.roll(rim, -1, 0) | np.roll(rim, 1, 1) | np.roll(rim, -1, 1)
    rim &= (out[..., 3] > 0) & (lo > 60)
    # pixel = a*ink + (1-a)*white, ink ~ the dark outline -> a from brightness, colour un-blended from white
    a = np.clip((255.0 - lo[rim]) / (255.0 - 40.0), 0.0, 1.0)
    keep = np.maximum(a, 1e-3)[:, None]
    rgb[rim] = np.clip((rgb[rim] - (1.0 - keep) * 255.0) / keep, 0, 255)
    out[rim, 3] = out[rim, 3] * a
    print('cleared %d enclosed white px (+%d rim px)' % (hole.sum(), rim.sum()))
    return (out + 0.5).astype(np.uint8)


def recolour_lining(rgba):
    a = rgba.astype(np.float32) / 255.0
    out = a.copy()
    h, w = a.shape[:2]
    for y in range(h):
        for x in range(w):
            if a[y, x, 3] < 0.1: continue
            hh, s, v = colorsys.rgb_to_hsv(*a[y, x, :3])
            if s > 0.45 and v > 0.35 and 185 / 360 < hh < 210 / 360:
                out[y, x, :3] = colorsys.hsv_to_rgb(TARGET_HUE, min(1.0, s * 1.05), v * VALUE_SCALE)
    return (np.clip(out, 0, 1) * 255 + 0.5).astype(np.uint8)


# Default: the original full-resolution pose (one frame). --sheet: the half-size 2x2 sheet with hair motion.
import sys
os.makedirs(DST, exist_ok=True)
for f in os.listdir(DST):
    if f.startswith('Slide_') and f.endswith('.png'): os.remove(os.path.join(DST, f))
if '--sheet' in sys.argv:
    sheet = np.asarray(Image.open(SRC).convert('RGB'))
    H, W = sheet.shape[0] // 2, sheet.shape[1] // 2
    for i in range(4):
        r, c = divmod(i, 2)
        rgb = sheet[r * H:(r + 1) * H, c * W:(c + 1) * W]
        rgba = np.dstack([rgb, cut_background(rgb)])
        Image.fromarray(recolour_lining(rgba), 'RGBA').save(os.path.join(DST, 'Slide_%02d.png' % i))
        print('Slide_%02d.png' % i, rgb.shape[1], 'x', rgb.shape[0], '(sheet: set 173 px/unit, pivot 341,21 in SlideSpriteImport.cs)')
else:
    rgba = np.asarray(Image.open(ORIGINAL).convert('RGBA'))   # transparent outside, but enclosed gaps are white
    Image.fromarray(recolour_lining(clear_enclosed_white(rgba)), 'RGBA').save(os.path.join(DST, 'Slide_00.png'))
    print('Slide_00.png', rgba.shape[1], 'x', rgba.shape[0], 'from', os.path.basename(ORIGINAL))
