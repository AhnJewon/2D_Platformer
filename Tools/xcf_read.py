"""Minimal GIMP XCF reader (8-bit RGB/RGBA, RLE/zlib/none) and compositor, no GIMP needed.

read_layers(path) -> (W, H, layers)   layers in file order (top first); each:
    dict(name, group, visible (incl. parent groups), opacity 0..1, offset (x, y), pixels RGBA uint8 array or None)
composite(path, skip=lambda name: False) -> RGBA uint8 array (H, W, 4)
    Normal-mode stack of the visible, non-group layers (layer opacity applied; blend modes ignored).
"""
import struct, zlib
import numpy as np

PROP_END, PROP_OPACITY, PROP_VISIBLE, PROP_OFFSETS, PROP_COMPRESSION = 0, 6, 8, 15, 17
PROP_GROUP_ITEM, PROP_ITEM_PATH, PROP_FLOAT_OPACITY = 29, 30, 33


def read_layers(path):
    d = open(path, 'rb').read()
    if not d.startswith(b'gimp xcf '): raise ValueError('not an XCF file: ' + path)
    ver = d[9:13]
    v = 0 if ver == b'file' else int(ver[1:4])
    P = 8 if v >= 11 else 4
    W, H, base = struct.unpack('>III', d[14:26])
    p = 26
    precision = 150
    if v >= 4:
        precision = struct.unpack('>I', d[p:p + 4])[0]; p += 4
    if v >= 4 and precision not in (100, 150, 0):
        raise ValueError('%s: only 8-bit images are supported (Image > Precision > 8 bit integer)' % path)

    def rp(q): return (struct.unpack('>Q', d[q:q + 8])[0] if P == 8 else struct.unpack('>I', d[q:q + 4])[0]), q + P

    def props(q):
        out = {}
        while True:
            t, l = struct.unpack('>II', d[q:q + 8]); q += 8
            out[t] = d[q:q + l]; q += l
            if t == PROP_END: return out, q

    img_props, p = props(p)
    comp = img_props[PROP_COMPRESSION][0] if PROP_COMPRESSION in img_props else 1
    ptrs = []
    while True:
        lp, p = rp(p)
        if not lp: break
        ptrs.append(lp)

    def rle(q, n):
        o = bytearray()
        while len(o) < n:
            c = d[q]; q += 1
            if c <= 126: o += bytes([d[q]]) * (c + 1); q += 1
            elif c == 127: k = struct.unpack('>H', d[q:q + 2])[0]; o += bytes([d[q + 2]]) * k; q += 3
            elif c == 128: k = struct.unpack('>H', d[q:q + 2])[0]; o += d[q + 2:q + 2 + k]; q += 2 + k
            else: k = 256 - c; o += d[q:q + k]; q += k
        return o, q

    def pixels(hp):
        w, h, bpp = struct.unpack('>III', d[hp:hp + 12])
        if bpp not in (1, 2, 3, 4): raise ValueError('%s: unsupported pixel format (bpp %d)' % (path, bpp))
        lv, _ = rp(hp + 12)
        lw, lh = struct.unpack('>II', d[lv:lv + 8]); q = lv + 8
        tx, ty = (lw + 63) // 64, (lh + 63) // 64
        tiles = []
        for _ in range(tx * ty):
            t, q = rp(q); tiles.append(t)
        tiles.append(None)
        img = np.zeros((lh, lw, bpp), np.uint8)
        for k in range(tx * ty):
            cx, cy = k % tx, k // tx
            tw, th = min(64, lw - cx * 64), min(64, lh - cy * 64)
            t = tiles[k]
            if comp == 1:
                planes = []
                for _ in range(bpp):
                    buf, t = rle(t, tw * th); planes.append(np.frombuffer(bytes(buf[:tw * th]), np.uint8))
                tile = np.stack(planes, -1).reshape(th, tw, bpp)
            elif comp == 2:
                raw = zlib.decompressobj().decompress(d[t:tiles[k + 1] if tiles[k + 1] else len(d)])
                tile = np.frombuffer(raw[:tw * th * bpp], np.uint8).reshape(th, tw, bpp)
            elif comp == 0:
                tile = np.frombuffer(d[t:t + tw * th * bpp], np.uint8).reshape(th, tw, bpp)
            else:
                raise ValueError('%s: unsupported compression %d' % (path, comp))
            img[cy * 64:cy * 64 + th, cx * 64:cx * 64 + tw] = tile
        if bpp == 4: return img
        if bpp == 3: return np.concatenate([img, np.full((lh, lw, 1), 255, np.uint8)], -1)
        if bpp == 2: return np.concatenate([np.repeat(img[..., :1], 3, -1), img[..., 1:]], -1)
        return np.concatenate([np.repeat(img, 3, -1), np.full((lh, lw, 1), 255, np.uint8)], -1)

    layers, by_path, n_top = [], {}, 0
    for lp in ptrs:
        q = lp
        w, h, typ, nl = struct.unpack('>IIII', d[q:q + 16]); q += 16
        name = d[q:q + nl - 1].decode('utf-8', 'replace'); q += nl
        pr, q = props(q)
        hp, q = rp(q)
        if PROP_ITEM_PATH in pr:
            path_ = tuple(struct.unpack('>%dI' % (len(pr[PROP_ITEM_PATH]) // 4), pr[PROP_ITEM_PATH]))
        else:
            path_ = (n_top,); n_top += 1
        vis = struct.unpack('>I', pr[PROP_VISIBLE])[0] != 0 if PROP_VISIBLE in pr else True
        if PROP_FLOAT_OPACITY in pr: op = struct.unpack('>f', pr[PROP_FLOAT_OPACITY])[0]
        elif PROP_OPACITY in pr: op = struct.unpack('>I', pr[PROP_OPACITY])[0] / 255.0
        else: op = 1.0
        group = PROP_GROUP_ITEM in pr
        L = dict(name=name, group=group, visible=vis, opacity=op, path=path_,
                 offset=struct.unpack('>ii', pr[PROP_OFFSETS]) if PROP_OFFSETS in pr else (0, 0),
                 pixels=None if group else pixels(hp))
        by_path[path_] = L
        layers.append(L)
    for L in layers:  # hidden parent group hides its children
        for k in range(1, len(L['path'])):
            parent = by_path.get(L['path'][:k])
            if parent is not None and not parent['visible']: L['visible'] = False
    return W, H, layers


def composite(path, skip=lambda name: False):
    W, H, layers = read_layers(path)
    out = np.zeros((H, W, 4), np.float32)
    for L in reversed(layers):
        if L['group'] or not L['visible'] or skip(L['name']) or L['pixels'] is None: continue
        px = L['pixels'].astype(np.float32) / 255.0
        ox, oy = L['offset']
        h, w = px.shape[:2]
        x0, y0, x1, y1 = max(ox, 0), max(oy, 0), min(ox + w, W), min(oy + h, H)
        if x0 >= x1 or y0 >= y1: continue
        src = px[y0 - oy:y1 - oy, x0 - ox:x1 - ox]
        dst = out[y0:y1, x0:x1]
        a = src[..., 3:4] * L['opacity']
        da = dst[..., 3:4]
        oa = a + da * (1 - a)
        rgb = np.where(oa > 0, (src[..., :3] * a + dst[..., :3] * da * (1 - a)) / np.maximum(oa, 1e-6), 0)
        out[y0:y1, x0:x1] = np.concatenate([rgb, oa], -1)
    return (np.clip(out, 0, 1) * 255 + 0.5).astype(np.uint8)
