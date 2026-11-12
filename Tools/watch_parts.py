"""Watch ArtSource/Parts and push every saved part into Unity automatically (keeps the rig).

  uv run --no-project --with numpy --with pillow python Tools/watch_parts.py      (Ctrl+C to stop)

Save a part in GIMP (Ctrl+S on ArtSource/Parts/<part>.xcf, or overwrite <part>.png) and, about a second later,
Tools/update_parts.py runs: Player.psb is rebuilt and the open Unity Editor reimports it.
Layers named REF... inside a part file are ignored, so keep the reference layer visible while painting.
"""
import os, sys, time, traceback

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, 'Tools'))
import update_parts

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
    folder = os.path.join(ROOT, cfg.get('parts', 'ArtSource/Parts'))
    print('watching', os.path.relpath(folder, ROOT), '- save a part in GIMP to sync it to Unity (Ctrl+C to stop)', flush=True)
    last = snapshot(folder)
    pending, changed_at = set(), 0.0
    while True:
        time.sleep(POLL)
        now = snapshot(folder)
        diff = {f for f in set(now) | set(last) if now.get(f) != last.get(f)}
        last = now
        if diff:
            pending |= diff; changed_at = time.time()
            continue
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
