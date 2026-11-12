"""Run a GIMP 3 batch script from Tools/gimp/ without opening the GIMP window.
Usage: python Tools/gimp_run.py <script_name_without_.py>
GIMP path: env GIMP_EXE, else the Microsoft Store alias, else `gimp-3` on PATH."""
import glob, os, shutil, subprocess, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

def gimp_exe():
    if os.environ.get('GIMP_EXE'): return os.environ['GIMP_EXE']
    alias = glob.glob(os.path.expanduser(r'~\AppData\Local\Microsoft\WindowsApps\GIMP.*\gimp-3.exe'))
    return alias[0] if alias else shutil.which('gimp-3') or shutil.which('gimp-3.exe')

def run(name):
    exe = gimp_exe()
    if not exe: raise SystemExit('GIMP 3 not found; set GIMP_EXE')
    script = os.path.join(ROOT, 'Tools', 'gimp', name + '.py').replace(os.sep, '/')
    env = dict(os.environ, PROJECT_ROOT=ROOT)
    r = subprocess.run([exe, '-i', '--batch-interpreter=python-fu-eval',
                        '-b', "exec(open(r'%s', encoding='utf-8').read())" % script, '--quit'],
                       env=env, capture_output=True, text=True, errors='replace')
    out = (r.stdout or '') + (r.stderr or '')
    ok = 'batch command executed successfully' in out
    if not ok: print(out[-3000:])
    return ok

if __name__ == '__main__':
    sys.exit(0 if run(sys.argv[1]) else 1)
