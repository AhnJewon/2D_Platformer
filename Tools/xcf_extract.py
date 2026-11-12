import struct, sys, json, os
import numpy as np
from PIL import Image
src, out = sys.argv[1], sys.argv[2]
os.makedirs(out, exist_ok=True)
d = open(src, 'rb').read()
v = int(d[10:13]); P = 8 if v >= 11 else 4
def rp(p): return (struct.unpack('>Q', d[p:p+8])[0] if P == 8 else struct.unpack('>I', d[p:p+4])[0]), p+P
def props(p):
    o = {}
    while True:
        t, l = struct.unpack('>II', d[p:p+8]); p += 8; o[t] = d[p:p+l]; p += l
        if t == 0: return o, p
W, H = struct.unpack('>II', d[14:22]); p = 30
_, p = props(p)
lps = []
while True:
    lp, p = rp(p)
    if not lp: break
    lps.append(lp)
def rle(buf, q, n):
    o = bytearray()
    while len(o) < n:
        c = buf[q]; q += 1
        if c <= 126: o += bytes([buf[q]]) * (c+1); q += 1
        elif c == 127: k = struct.unpack('>H', buf[q:q+2])[0]; q += 2; o += bytes([buf[q]]) * k; q += 1
        elif c == 128: k = struct.unpack('>H', buf[q:q+2])[0]; q += 2; o += buf[q:q+k]; q += k
        else: k = 256-c; o += buf[q:q+k]; q += k
    return o, q
layers = []
for i, lp in enumerate(lps):
    q = lp
    w, h, t, nl = struct.unpack('>IIII', d[q:q+16]); q += 16
    name = d[q:q+nl-1].decode('utf-8'); q += nl
    pr, q = props(q)
    ox, oy = struct.unpack('>ii', pr[15]) if 15 in pr else (0, 0)
    vis = struct.unpack('>I', pr[8])[0] if 8 in pr else 1
    grp = 29 in pr
    path = list(struct.unpack('>%dI' % (len(pr[30])//4), pr[30])) if 30 in pr else []
    hp, q = rp(q)
    rec = dict(index=i, name=name, visible=bool(vis), group=grp, path=path, offset=[ox, oy], size=[w, h])
    if not grp:
        hw, hh, bpp = struct.unpack('>III', d[hp:hp+12]); lv, _ = rp(hp+12)
        lw, lh = struct.unpack('>II', d[lv:lv+8]); tq = lv+8
        img = np.zeros((lh, lw, bpp), np.uint8)
        tx = (lw+63)//64; ty = (lh+63)//64
        tps = []
        for k in range(tx*ty):
            tp, tq = rp(tq); tps.append(tp)
        for k, tp in enumerate(tps):
            cx, cy = k % tx, k // tx
            tw = min(64, lw-cx*64); th = min(64, lh-cy*64)
            qq = tp
            for c in range(bpp):
                buf, qq = rle(d, qq, tw*th)
                img[cy*64:cy*64+th, cx*64:cx*64+tw, c] = np.frombuffer(bytes(buf[:tw*th]), np.uint8).reshape(th, tw)
        if bpp == 4:
            canvas = np.zeros((H, W, 4), np.uint8)
            canvas[oy:oy+lh, ox:ox+lw] = img
            fn = f"L{i:02d}.png"
            Image.fromarray(canvas, 'RGBA').save(os.path.join(out, fn))
            rec['file'] = fn
            a = canvas[..., 3]; ys, xs = np.nonzero(a)
            rec['bbox'] = [int(xs.min()), int(ys.min()), int(xs.max())+1, int(ys.max())+1] if len(xs) else None
        rec['bpp'] = bpp
    layers.append(rec)
json.dump(dict(canvas=[W, H], layers=layers), open(os.path.join(out, 'layers.json'), 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
for r in layers: print(r['index'], r['name'], r.get('bbox'), 'vis' if r['visible'] else 'HID')
