# Mouse-aim melee and backpedal references

Code: `Assets/Scripts/PlayerCombat.cs`, `PlayerController2D.cs` (facing, backpedal speed) and `PlayerDraftClips.MakeBackpedalClip`.

## Attack style (Dungreed-like mouse aim)
- Dungreed is a mouse-aimed side-scroller with "swords to sniper rifles"
  (Steam page: https://store.steampowered.com/app/753420/Dungreed/; namu wiki: https://namu.wiki/w/던그리드).
  Neither page spells out the controls, so this is the behaviour we built:
  - She always faces the cursor.
  - The weapon points at the cursor.
  - Left click swings through the aim direction, alternating high → low → high, and leaves a crescent slash trail.
  - Holding the button repeats the attack.
  - Dash goes the way you move.
- One itch.io sword-swing devlog notes the swing always sweeps the same way around the aim toward the cursor, and
  that hitting several enemies in one swing is intended. Here a swing hits every target inside a ±75° arc,
  up to 1.7 u from the shoulder, once per swing.
- Values: 0.16 s sweep, 0.3 s between attacks (about 3.3 per second), blade resting 70° off the aim line,
  0.045 s hit-stop.

## Whole-body swing (v2, from non-deformed games and sword technique)
Dungreed's swing is only a floating weapon, which works because its art is heavily deformed. A rigged, realistically
proportioned character needs the whole body behind the cut.
- SLYNYRD Pixelblog 9, melee attacks (https://www.slynyrd.com/blog/2018/9/8/pixelblog-9-melee-attacks):
  - Ready stance, then about a 90°+ arc.
  - The weapon can stretch a little at full extension.
  - Hold the follow-through slightly longer to give the weapon weight.
  - One fast smear along the arc.
- GDQuest "juicy attack" (https://www.gdquest.com/library/juicy_attack):
  - One frame of pull-back anticipation, but not a long wind-up or the controls feel slow.
  - Smear only during the forward swing.
  - Eased swing end, hit blink, knockback that slows down, short hit-stop, impact particles.
- Sabre cut technique:
  - The blade gathers speed through rotation before the body commits
    (https://academieduello.com/?p=4434).
  - Step and cut finish together, with the front heel landing as the blade arrives
    (https://www.bookandsword.com/?p=12408).
  - In the lunge the pelvis turns toward the sword side (https://core.ac.uk/display/81954444).
- Katana ZERO: a left-click slash aimed at the cursor that carries the character a little toward it
  (https://store.steampowered.com/app/460950/Katana_ZERO/). This is from memory of playing it; no written source
  was found.

Implemented (PlayerCombat.SwingShape). Times total 0.3 s:
1. **Wind-up, 0.045 s:**
   - The blade is pulled 30° past its rest angle.
   - Chest coils 14° and pelvis 7° away from the cut. The free arm comes forward.
2. **Strike, 0.075 s:**
   - The blade sweeps past the aim line to 45° beyond the opposite rest angle.
   - The arm follows 70% of the blade angle and extends fully.
   - Chest and pelvis turn into the cut; the free arm swings back.
   - The hit lands at 35% of the strike, and the slash trail and lunge start with it.
   - On the ground she takes a 3.5 u/s step in the aim direction. Standing still, she also takes a lunge stance: front
     knee forward and bent, rear leg stretched, hips 0.03 lower.
   - In the air, her fall stops once per jump (vertical speed at least 1.5).
3. **Follow-through, 0.18 s:** held for a quarter of it, then settles back to rest.

## Backpedal (moving away from the cursor)
- Backward-walking biomechanics:
  - Joint angles are close to a time-reversed forward walk (Gait & Posture 2013, via
    https://www.scribd.com/document/486803879/lee2013-pdf).
  - The toes touch down first.
  - Speed, cadence-normalised stride and stride length are lower, and stance takes a larger share of the cycle.
  - Knee range of motion is smaller
    (https://salford-repository.worktribe.com/output/1396748/comparison-of-biomechanical-characteristics-of-the-knee-joint-during-forward-walking-and-backward-walking,
    https://chhs.source.colostate.edu/backward-walking-fitness-fad-or-balance-hack/).
- Football defensive-back backpedal coaching (https://www.stack.com/?p=90961,
  https://www.active.com/football/articles/6-tips-for-coaching-defensive-backs,
  https://www.glazierclinics.com/football-coach-resources/free-football-drills-defensive-back-backpedal-drill):
  - Low stance with the chest forward over the knees and the weight on the balls of the feet, never the heels.
  - Short, quick steps with the feet kept low.
  - Arms bent about 90°.
  - Never at full speed.
- Game-dev notes: aiming games need a separate walk-backward animation; playing the forward cycle at negative speed
  is the usual cheap fallback
  (https://forum.godotengine.org/t/animation-does-not-change-when-continuously-pressed-the-key-and-changed-the-mouse-position/5576,
  https://forum.gdevelop.io/t/change-object-animation-top-down-style-with-mouse-position-and-possible-bug/44038?page=2).
  Here the standing backpedal has its own clip. The crouch walk does use the reversed cycle (Crouch state speed =
  MoveDir), which the time-reversal finding above supports.

## Values used (draft)
- Speed and cycle:
  - Backpedal speed 2.6 u/s, about half the run.
  - Cycle 0.46 s for two quick steps, stride 0.6, so the feet do not slide.
- Body:
  - Leg joint height 1.38 (standing 1.49), so the knees are a little bent.
  - Pelvis 9° and chest 9° forward. Neck +5°, head +9°.
- Feet:
  - Each foot touches down behind the hips, toes first (foot −24°), then the heel lowers.
  - The planted foot travels forward under her. The toes lift first at the end of stance.
  - In the swing the foot lifts only 0.08.
- Hair, coat and skirt trail forward (+), because she moves backwards.
- Arms bend about 80–85° with small pumps. The near arm is re-posed every frame by the weapon aim (2-bone IK).

## Grip (v3)
- The sabre is rigid in the fist.
  - In the drawn fist (bind pose, arm hanging), the blade points straight forward: 90° from the forearm.
  - The wrist can bend the blade to 15–110° from the forearm. The arm makes the rest of the swing.
  - The hand bone is set absolutely from the forearm every frame, so it never accumulates or spins.
- The guards are asymmetric: high guard is the blade 70° above the aim, low guard is 55° below it (arm hanging,
  blade pointing forward-down). Wind-up adds 25° and the overshoot is 15°.
- Art: NovelAI v4.5 full, seed 20431, 1536x640, white background
  (ArtSource/NAI_V5_rig_v3/weapons/sabre_nai45_seed20431.png).
  - Cut out with Tools/import_sabre.py.
  - 1.8 u long at 400 px/unit; a real sabre is about 0.55× its wielder's height.
  - The wiki style preset could not be read because the wiki server was down, so the prompt was matched to the
    character by hand.

## Posture (v4): real references only
AI-generated pose images are not used as references.
- **Mega Man Zero 3, Z-Saber sheet** (Capcom/Inti Creates; sprites-inc "zero_z3sabresheet.gif", viewed through image
  search because the site redirects):
  - Every ground slash is done with the knees bent and a low centre of gravity.
  - The trunk leans 10–20° forward as one piece, with no bend at the waist and the head kept in line.
  - The off arm stays tucked to the body.
  - The cut ends with the sword arm forward-low and the blade continuing the line of the forearm.
  - The slash arc is large and covers the front.
- **Castlevania: Symphony of the Night, Alucard sword slash GIF**
  (https://www.fightersgeneration.com/news2020/char2/alucard-symphony-of-the-night-sword-slash.gif):
  - The body stays upright under a big overhead-to-front arc.
  - That GIF shows a special sword, so it was only used for the upright trunk.
- **Kendo men strike**
  (https://kendojidai.com/2025/08/04/winning-through-proper-posture-and-pressure-iwatate-saburo/,
  https://kendojidai.com/2021/04/19/essential-left-foot-techniques-kamei-toru/):
  - The strike is driven from the feet and hips.
  - Leaning the upper body forward is a named fault; the spine stays aligned.
- **Sabre lunge (pelvis turns toward the sword side) and step-with-cut timing**: sources as above.

Implemented values:
- **Trunk:** leans only from the pelvis (12° forward on the strike, 4° back on the wind-up). There is no spine bend, and
  the neck takes back 70% of the lean so the head stays level.
- **Lunge on an attack in place:**
  - The front foot steps 0.5 u forward and the rear foot slides 0.35 u back.
  - The hips drop 0.22 u, so the front knee bends.
  - The rear heel lifts 22°.
  - Legs are solved with 2-bone IK, so the feet stay on the floor.
- **Free hand:** goes back to the hip (upper arm −45°, forearm +80°).
- **Wrist:** the blade may now be −5…110° from the forearm, so the extended cut has the blade nearly in line with the
  arm. The low guard is 30° under the aim (the cut ends forward-low), and the overshoot is 10°.
- **Slash:** NAI v4.5 crescent (seed 5150), band thinned to 50%, centred on the shoulder with its radius on the blade-tip
  path (about 2 u). Hit reach is 2.1 u.
- **Dummy:** NAI v4.5 (seed 8843), 2.3 u tall.
- **Grip side (v5):** the sabre sprite is mirrored across its blade axis (`PlayerCombat.flipWeapon`). The knuckle bow, and
  with it the cutting edge, now sits on the finger side of the drawn fist. The blade leaves the fist on the thumb and
  index side, and the pommel on the little-finger side.
- **Sabre art and wrist (v6):**
  - The sabre was regenerated (NAI v4.5, seed 44120): a straight, clean blade with a bow guard above and below the grip.
  - The cutout (Tools/import_sabre.py) fits the blade edges as lines, so the white glare on the blade is kept.
  - The knuckle bow is drawn on top, so `flipWeapon` is now off.
  - The wrist is limited to blade-to-forearm 40–125° (neutral grip 90°), so it never bends more than about 50° and the
    arm makes the swing. The low guard is 25° under the aim.

## Hand poses (v7)
- Hands are swapped per state instead of using one drawn fist (`HandPoses.cs`). The art is NAI v4.5 gloves cut by
  Tools/import_hands.py, pivoted at the wrist, colour-matched to the rig glove, with any skin or cuff removed.
  - **GripInline (seed 12092):** fist with the sword in line with the wrist. Used on cuts and thrusts, so the arm
    reaches out along the blade and the wrist stays straight. The guards and wind-ups keep the rig's own fist, which
    holds the blade about 90° off the forearm. The drawing switches by the wrist angle (45/55° hysteresis), and the arm
    target blends between the two grips.
  - **Open (seed 12101):** relaxed free hand when standing or walking.
  - **Spread (seed 12102):** free hand while attacking (the arm swings back for balance) and in the air.
  - The rig fist is kept for running.
- Rejected generations: a glossy leather glove with a strap, a fabric texture, and orange fingertips (fingerless).
- From the side, the free hand is mostly hidden behind the body; it shows when the arm swings.
- **Sword hand (v8):**
  - Generated with NAI V5 and the wiki style preset `nai-v5-yamada9000-fis-mix` (euler_ancestral, CFG 5.5, rescale
    0.6, 28 steps), seed 30102.
  - It shows the back of the right hand with the thumb on top. The hilt crosses the fist at 77.4° to the
    wrist→knuckle axis, as a real fist holds it.
  - The generated hilt stubs are erased so the game sabre shows.
  - It replaces the rig fist and the v4.5 in-line grip; the in-line grip showed the finger side and looked wrong
    (`useInlineGrip` is off).
  - The wrist range is now 25–110° blade-to-forearm.

## Combo (v9): cut → thrust → rising cut
- **Data:** `MeleeWeapon` asset (`Assets/Character/Weapons/Sabre.asset`), steps in `MeleeWeapon.DefaultSabre()`.
  - Each step sets its blade path relative to the aim (wind-up, through, settle), timing, arm reach in the wind-up,
    hit (damage, reach, arc, knockback and knock-up, hit-stop, hit point), body (lean in the wind-up and on the strike,
    lunge speed, stance) and effect.
  - A click during a step, or within `comboWindow` (0.4 s) after it, continues the combo. The next step may start once
    half the recovery has passed (`cancelAt`). After a pause the blade returns to the high guard and the combo
    restarts.
- **1 Cut:** overhead (+95°) to forward-low (−40°), with a step. References: Mega Man Zero 3 ground slash, kendo men.
- **2 Thrust:**
  - Wind-up: the arm pulls back (wrist 0.30 from the shoulder) with the trunk leaning back 6°.
  - Strike: the arm fully extends along the aim while the front foot lunges 0.75 u, the hips drop 0.28 u, the trunk
    leans 16° forward and the lunge speed is 6.
  - Narrow ±18° arc, but the blade line itself also counts as a hit; reach 2.6 u.
  - Effect: a thin streak (procedural) instead of a crescent.
  - References: fencing lunge. Skilled fencers start the sword arm before the front foot, the elbow is at full
    extension around the touch, and the rear leg straightens as the front knee bends.
    - https://ojs.ub.uni-konstanz.de/cpa/article/view/6528/5892
    - https://journal.unj.ac.id/unj/index.php/jor/article/view/66428
    - https://davidbiggs.substack.com/p/the-lunge-and-its-mechanics-part
- **3 Rising cut:**
  - From low behind the hip (−65°) up to high (+105°), settling in the high guard. The body crouches over in the
    wind-up (10° forward) and rises back on the cut (8° back).
  - Finisher: damage 15, knockback turned 55° up, 0.08 s hit-stop.
  - References: longsword Unterhau, a rising cut from below that steps in, drives the edge up from the hip toward the
    opposite shoulder and ends high, like "punching up with the fists".
    - https://swordfight.uk/straight-cuts/
    - https://sevenswords.uk/?p=2082
    - https://thearma.org/forum/viewtopic.php?p=21421
- **Thrust hand:** two NAI V5 (preset) attempts at a dedicated in-line grip (seeds 30201, 30202) showed the finger side,
  so they were not used. The thrust uses the Grip drawing with the wrist cocked (blade 25–35° above the forearm).
