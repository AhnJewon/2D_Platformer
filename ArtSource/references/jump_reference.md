# Jump pose reference (draft clips)

## Sources (looked at 2026-11-12)
- Toon Boom Learn, "Deconstructing a Jump" — vertical (standing) jump key sheet, frames 1-10:
  https://learn.toonboom.com/modules/jump-animation1/topic/deconstructing-a-jump
- Eadweard Muybridge, *Animal Locomotion* (1887), Plate 160 "Jumping, running broad jump" — side-view photos, public
  domain, National Gallery of Art: https://www.nga.gov/artworks/166658-plate-number-160-jumping-running-broad-jump
- Game-animation practice: MoCap Online platformer guide (https://mocaponline.com/blogs/mocap-news/platformer-animation-guide),
  Game Developer "12 principles of animation in video games", Celeste/Hollow Knight game-feel write-ups.

## Game rules taken from the game references
- Take-off is instant: the physics jump fires on the button; the crouch is only 2 frames and overlaps the rise.
- Air poses are chosen by the vertical speed, not by clip time (rise / apex / fall), so short hops and long jumps both
  hit the apex pose at the real apex. Every rise frame must read as "going up".
- Fall telegraphs the landing (legs coming down, arms out for balance).
- Landing: frames 0-2 full compression, then back to idle/run; ~0.18 s, and moving input cancels into Walk/Run early.
- Standing and moving jumps use two air sets blended by horizontal speed (Speed): standing = vertical, symmetric poses,
  moving = running-broad-jump poses.

## Conventions
Character faces +X. Offsets from the bind pose in degrees (+ = counter-clockwise): thigh + = forward/up, shin - = knee
bends, foot - = toes point down, spine - = leans forward, arm upper + = forward/up (~+160 overhead), forearm + = elbow bends.
"Flat" = foot counter-rotated so the sole stays on the floor; hip height solved so the lower foot touches the floor.

## Standing set (Toon Boom vertical jump)
| Pose | Frame | Torso | Legs | Arms |
|---|---|---|---|---|
| Crouch | 3 | spine -30 | thighs +55, knees -95, flat | hanging back -25 |
| Push (end of JumpStart, both jumps) | 4 | spine -4, straight | thighs -6/-2, knees -6, toes -25 | swinging forward-up +80 / +70 |
| Rise (push/stretch) | 4-5 | spine +2, head up | thighs -4, knees -6, toes pointed -25 | swung up +140 / +125 |
| Apex (tuck) | 6 | spine -8 | thighs +65, knees -100 | coming down forward +55 / +45, elbows 30 |
| Fall (contact prep) | 7 | spine 0 | thighs +12, knees -22, toes -5 | slightly forward-up +35 / +25 |
| Impact | 8 | spine -28 | thighs +55, knees -100, flat | forward-down +30 |

## Moving set (game style, replaces the broad-jump set on 2026-11-12)
Games do not use the athletic long-jump pose (legs together reaching forward). The running jump reads as a stride
carried into the air: one knee up, the other leg hanging down; arms spread for balance on the way down.
References: SLYNYRD "Pixelblog 60 - Side View Run 'N Gun" (up pose = knee up stride, down pose = limbs gathered;
no anticipation) https://www.slynyrd.com/blog/2026/1/26/side-view-run-n-gun and OpenGameArt "Platformer Animations"
(Castlevania / Ninja Gaiden style human base, jump & fall row) https://opengameart.org/content/platformer-animations

| Pose | Reference | Lean (hip/spine) | Lead (front) leg | Trail (rear) leg | Arms |
|---|---|---|---|---|---|
| Rise | OGA jump row 4-5, SLYNYRD up | -7 / -8 (~15 fwd) | thigh +65, knee -75, toes -10 | thigh -12, knee -20, toes -20 (hangs down/back) | far arm forward +45 (elbow 40), near arm back -35 |
| Apex | OGA 6, SLYNYRD down | -4 / -4 | thigh +60, knee -80 | thigh +20, knee -45 (following up) | spreading: far +65, near -45 |
| Fall | OGA 7-8 | 0 / 0 (upright) | thigh +35, knee -55, toes +5 | thigh +3, knee -8, toes -10 (straight down, ready to land) | spread: far forward-up +85, near back-up -70 |

The first draft used Muybridge Plate 160 (running broad jump) for this set; kept below the lean table for reference.

## Whole-body lean (read from the same frames)
The torso angle in the references comes mostly from the pelvis tilting, not just the chest bending. The coat and skirt
hang from the pelvis (hip bone), so without a pelvis tilt the whole silhouette stays upright. Total forward lean is
split about half pelvis (hip bone) / half chest (spine); on the ground the thighs are compensated so the feet stay put.

| Pose | Reference | Total torso lean | hip | spine |
|---|---|---|---|---|
| Crouch | Toon Boom 3 | ~45 fwd | -22 | -23 |
| Standing rise (stretch) | Toon Boom 5 | ~5 back (arched) | +3 | +4 |
| Standing apex (tuck) | Toon Boom 6 | ~15 fwd | -8 | -8 |
| Standing fall | Toon Boom 7 | ~0 | +2 | 0 |
| Impact | Toon Boom 8 / Muybridge 7-8 | ~40 fwd | -18 | -22 |
| Moving rise (game) | OGA / SLYNYRD up | ~15 fwd | -7 | -8 |
| Moving apex (game) | OGA / SLYNYRD down | ~8 fwd | -4 | -4 |
| Moving fall (game) | OGA 7-8 | ~0 | 0 | 0 |
| Run (gait refs, ~15 fwd) | running biomechanics | ~19 fwd | -7 | -12 |

Hair/coat: pushed down while rising, lifted up and back while falling, overshoot down on impact.
