"""Where art lives, per character. Every Tools/ script takes its paths from here.

ArtSource/
  Characters/<Name>/
    Rig/        Parts/*.xcf (one per rig part), layers/ (PSB layers), layer_order.json, layer_bounds.json,
                bone_layout.json, parts_source.json, guides/, rig state files
    Sprites/    loose sprites of this character (hand poses, expressions): <name>.xcf + sprites.json
    Source/     NovelAI originals and derived intermediates (character art, parts, slide frames, hands, face)
  Weapons/      Sprites/ (weapon art, effects) + Source/ (NAI originals)
  Props/        Sprites/ (training dummy...) + Source/
  References/   motion and game references (shared)
  backups/      automatic backups (not committed)
Assets/
  Characters/<Name>/   Player.psb, Player.prefab, Animations/, Hands/, Face/, Slide/
  Weapons/             weapon sprites and WeaponData assets
  Props/
The character is picked with --character <Name> (where supported) or the CHARACTER environment variable;
the default is Officer.
"""
import glob, os

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ART = os.path.join(ROOT, 'ArtSource')
CHARACTER = os.environ.get('CHARACTER', 'Officer')
BACKUPS = os.path.join(ART, 'backups')
REFERENCES = os.path.join(ART, 'References')
WEAPONS = os.path.join(ART, 'Weapons')
PROPS = os.path.join(ART, 'Props')


def char_dir(name=None): return os.path.join(ART, 'Characters', name or CHARACTER)
def rig_dir(name=None): return os.path.join(char_dir(name), 'Rig')
def source_dir(name=None): return os.path.join(char_dir(name), 'Source')
def sprites_dir(name=None): return os.path.join(char_dir(name), 'Sprites')
def assets_dir(name=None): return os.path.join(ROOT, 'Assets', 'Characters', name or CHARACTER)
def psb(name=None): return os.path.join(assets_dir(name), 'Player.psb')
def prefab_rel(name=None): return 'Assets/Characters/%s/Player.prefab' % (name or CHARACTER)
def psb_rel(name=None): return 'Assets/Characters/%s/Player.psb' % (name or CHARACTER)


def sprite_sets():
    """every Sprites/ folder that has a sprites.json (characters, weapons, props)"""
    return sorted(os.path.dirname(p) for p in glob.glob(os.path.join(ART, '**', 'Sprites', 'sprites.json'), recursive=True))


def rel(path): return os.path.relpath(path, ROOT).replace(os.sep, '/')
