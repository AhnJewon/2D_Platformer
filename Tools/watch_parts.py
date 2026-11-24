"""Watch ArtSource/Characters/Officer/Rig/Parts and push every saved part into Unity automatically (keeps the rig).

  uv run --no-project --with numpy --with pillow python Tools/watch_parts.py      (Ctrl+C to stop)

Save a part in GIMP (Ctrl+S on ArtSource/Characters/Officer/Rig/Parts/<part>.xcf, or overwrite <part>.png) and, about a second later,
Tools/update_parts.py runs: Player.psb is rebuilt and the open Unity Editor reimports it.
Layers named REF... inside a part file are ignored, so keep the reference layer visible while painting.
Also watches ArtSource/Characters/Officer/Sprites (hand poses, sabre, slash, dummy; made by Tools/make_sprite_files.py): a saved sprite
XCF runs Tools/update_sprites.py for that sprite only.
"""
import os, sys, time, traceback

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, 'Tools'))
import update_parts
import update_sprites

SETTLE = 1.0   # seconds a file must stay unchanged before syncing (GIMP writes in several steps)
POLL = 0.5


def snapshot(folder):
    out = {}
    for f in os.listdir(folder):
        if f.lower().endswith(('.xcf', '.png')):
            try:
                st = os.stat(os.path.join(folder, f)); out[f] = (st.st_mtime_ns, st.st_size)
            except OSError:
                pass
    return out


def main():
    cfg = update_parts.load(os.path.join(update_parts.ART, 'parts_source.json'))
    folder = os.path.join(ROOT, cfg.get('parts', update_parts.P.rel(os.path.join(update_parts.ART, 'Parts'))))
    print('watching', os.path.relpath(folder, ROOT), '- save a part in GIMP to sync it to Unity (Ctrl+C to stop)', flush=True)
    sets = update_sprites.PP.sprite_sets()
    for d in sets: print('watching', os.path.relpath(d, ROOT), flush=True)
    def snap_sets(): return {os.path.join(d, f): v for d in sets for f, v in snapshot(d).items()}
    last = snapshot(folder)
    last_s = snap_sets()
    pending, pending_s, changed_at = set(), set(), 0.0
    while True:
        time.sleep(POLL)
        now = snapshot(folder)
        now_s = snap_sets()
        diff = {f for f in set(now) | set(last) if now.get(f) != last.get(f)}
        diff_s = {f for f in set(now_s) | set(last_s) if now_s.get(f) != last_s.get(f) and f.lower().endswith('.xcf')}
        last, last_s = now, now_s
        if diff or diff_s:
            pending |= diff; pending_s |= diff_s; changed_at = time.time()
            continue
        if pending_s and time.time() - changed_at >= SETTLE:
            names = sorted(os.path.splitext(os.path.basename(f))[0] for f in pending_s)
            print('\n[%s] sprites changed: %s' % (time.strftime('%H:%M:%S'), ', '.join(names)), flush=True)
            pending_s = set()
            try:
                update_sprites.main(names)
            except Exception:
                traceback.print_exc()
            sys.stdout.flush()
        if pending and time.time() - changed_at >= SETTLE:
            print('\n[%s] changed: %s' % (time.strftime('%H:%M:%S'), ', '.join(sorted(pending))), flush=True)
            pending = set()
            try:
                update_parts.main(['--source', 'parts'])
            except SystemExit as e:
                if e.code not in (None, 0): print('update stopped:', e.code, flush=True)
            except Exception:
                traceback.print_exc()
            sys.stdout.flush()


if __name__ == '__main__':
    try:
        main()
    except KeyboardInterrupt:
        print('\nstopped')
