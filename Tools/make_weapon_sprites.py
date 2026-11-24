"""Draws the placeholder weapon art in Assets/Weapons at the rig's scale (400 px/unit):
 - Saber.png: an officer's sabre to match the military coat - slightly curved silver blade, gold knuckle-bow guard,
   navy grip. Drawn pointing +X, grip at the left; the import pivot (WeaponSpriteImport.cs) is the middle of the grip.
 - Slash.png: the crescent swing trail (white, alpha fades toward the tail and the inner edge), drawn for a
   counter-clockwise swing around the image centre, opening toward +X.
 - Dummy.png: a training dummy (post + straw sack + crossbar).
Outlines use the rig's dark ink colour. Drawn at 4x and downsampled for clean anti-aliasing.
Usage: python Tools/make_weapon_sprites.py"""
import math, os
from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DST = os.path.join(ROOT, 'Assets', 'Weapons')
SS = 4
INK = (38, 32, 46, 255)


def saber():
    W, H = 480, 96
    im = Image.new('RGBA', (W * SS, H * SS), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    s = lambda v: v * SS
    cy = H / 2
    # blade: from the guard (x=96) to the tip (x=470), slight upward curve (edge on the bottom)
    def curve(x): return -0.00022 * (x - 96) ** 2
    top, bot = [], []
    for i in range(0, 61):
        x = 96 + (470 - 96) * i / 60
        w = 8.5 * (1 - 0.4 * (i / 60) ** 2)
        c = cy + curve(x)
        top.append((s(x), s(c - w))); bot.append((s(x), s(c + w * 0.8)))
    tip = (s(478), s(cy + curve(478) - 1))
    blade = top + [tip] + bot[::-1]
    d.polygon(blade, fill=INK)
    inner = [(x, y + SS * 2.2) for x, y in top[1:-2]] + [(s(462), s(cy + curve(462) - 2))] + [(x, y - SS * 2.2) for x, y in bot[1:-2][::-1]]
    d.polygon(inner, fill=(196, 204, 222, 255))
    # bright edge highlight + fuller groove
    hl = [(x, y + SS * 3.2) for x, y in top[2:-6]]
    d.line(hl, fill=(250, 252, 255, 255), width=SS * 2)
    groove = [(s(110 + (380 - 110) * i / 30), s(cy + curve(110 + (380 - 110) * i / 30) - 0.5)) for i in range(31)]
    d.line(groove, fill=(140, 150, 175, 255), width=SS * 2)
    # guard: gold cross-piece + knuckle bow (D-shape below the grip)
    d.rounded_rectangle([s(86), s(cy - 15), s(100), s(cy + 15)], radius=s(4), fill=INK)
    d.rounded_rectangle([s(88.5), s(cy - 12.5), s(97.5), s(cy + 12.5)], radius=s(3), fill=(214, 172, 74, 255))
    d.arc([s(20), s(cy + 2), s(96), s(cy + 30)], 0, 180, fill=INK, width=s(6))
    d.arc([s(22), s(cy + 4), s(94), s(cy + 28)], 0, 180, fill=(214, 172, 74, 255), width=s(3))
    # grip (navy, wrapped) + pommel
    d.rounded_rectangle([s(28), s(cy - 8), s(88), s(cy + 8)], radius=s(5), fill=INK)
    d.rounded_rectangle([s(30.5), s(cy - 5.5), s(86), s(cy + 5.5)], radius=s(4), fill=(28, 52, 98, 255))
    for x in range(36, 86, 8):
        d.line([(s(x), s(cy - 5)), (s(x + 5), s(cy + 5))], fill=(70, 98, 150, 255), width=SS * 2)
    d.ellipse([s(14), s(cy - 11), s(36), s(cy + 11)], fill=INK)
    d.ellipse([s(17), s(cy - 8), s(33), s(cy + 8)], fill=(214, 172, 74, 255))
    d.ellipse([s(21), s(cy - 5), s(27), s(cy + 1)], fill=(250, 226, 150, 255))
    return im.resize((W, H), Image.LANCZOS)


def slash():
    N = 512
    im = Image.new('RGBA', (N, N), (0, 0, 0, 0))
    px = im.load()
    c = N / 2
    r_out, r_in = N * 0.49, N * 0.27
    a0, a1 = -105, 105   # the sweep: wound-up (rest 70 + 30) to the follow-through (rest 70 + 45), counter-clockwise
    for y in range(N):
        for x in range(N):
            dx, dy = x + 0.5 - c, c - (y + 0.5)
            r = math.hypot(dx, dy)
            if r < r_in * 0.8 or r > r_out: continue
            a = math.degrees(math.atan2(dy, dx))
            if a < a0 or a > a1: continue
            u = (a - a0) / (a1 - a0)                  # 0 tail .. 1 head
            # crescent: thick near the head, thin at the tail
            th = (r_out - r_in) * (0.12 + 0.88 * u ** 1.2)
            rin = r_out - th
            if r < rin: continue
            v = (r - rin) / max(th, 1)                # 0 inner .. 1 outer edge
            alpha = min(1.0, 0.15 + 1.1 * u ** 1.1) * (0.5 + 0.5 * v ** 1.2)
            alpha *= min(1, (a1 - a) / 6)             # soft head tip
            alpha *= min(1, (r_out - r) / 3)          # anti-aliased outer edge
            core = max(0, (v - 0.75) / 0.25)
            col = (int(150 + 105 * core), int(205 + 50 * core), 255)
            px[x, y] = col + (int(255 * max(0, min(1, alpha))),)
    return im


def dummy():
    W, H = 300, 760
    im = Image.new('RGBA', (W * SS, H * SS), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    s = lambda v: v * SS
    def ob(box, fill, r=10, o=4):
        d.rounded_rectangle([s(box[0] - o), s(box[1] - o), s(box[2] + o), s(box[3] + o)], radius=s(r + o), fill=INK)
        d.rounded_rectangle([s(v) for v in box], radius=s(r), fill=fill)
    ob((136, 300, 164, 740), (132, 92, 58, 255), 4)            # post
    ob((96, 720, 204, 748), (110, 76, 48, 255), 6)              # base
    ob((30, 300, 270, 326), (132, 92, 58, 255), 6)              # crossbar
    ob((70, 210, 230, 560), (214, 186, 120, 255), 60)           # straw body
    for y in (300, 420):
        d.line([(s(74), s(y)), (s(226), s(y))], fill=(150, 110, 70, 255), width=s(8))   # ropes
    for i in range(14):
        x = 90 + i * 10
        d.line([(s(x), s(240 + (i % 3) * 30)), (s(x + 4), s(530 - (i % 4) * 25))], fill=(190, 160, 96, 255), width=s(2))
    ob((96, 70, 204, 200), (222, 196, 132, 255), 54)            # head sack
    d.line([(s(150), s(200)), (s(150), s(214))], fill=INK, width=s(10))
    # target mark
    d.ellipse([s(118), s(360), s(182), s(424)], fill=INK)
    d.ellipse([s(123), s(365), s(177), s(419)], fill=(200, 60, 60, 255))
    d.ellipse([s(136), s(378), s(164), s(406)], fill=(240, 230, 210, 255))
    return im.resize((W, H), Image.LANCZOS)


os.makedirs(DST, exist_ok=True)
saber().save(os.path.join(DST, 'Saber.png'))
slash().save(os.path.join(DST, 'Slash.png'))
dummy().save(os.path.join(DST, 'Dummy.png'))
print('wrote Saber.png, Slash.png, Dummy.png to', DST)
