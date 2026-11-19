# Crouch / crouch walk references

Used by `PlayerDraftClips.MakeCrouchClips` (clips `Player_Crouch_Draft`, `Player_CrouchWalk_Draft`).

## Sources
- Dimensions.com, "Crouching - Women (Side)": https://www.dimensions.com/element/crouching-women-side
  (side-view outline drawings). Figure 2 is the base for the crouch idle: front foot flat, front shin near
  vertical with the knee raised to about hip/chest height, rear knee low with the heel lifted onto the toes,
  trunk folded forward over the thighs, head up.
- Animation Mentor, "Animating a sneak walk cycle with personality":
  https://www.animationmentor.com/blog/tutorial-animating-sneak-walk-cycle-with-personality/
  The sneak uses the same key poses as a walk (contact, down, passing, up). The hips rise a little at passing
  even though the knees stay bent, and the steps are short.
- Lynda / LinkedIn Learning, "2D Animation: Character & Attitude Walk Cycles": a sneak starts and ends on a
  contact pose that already reads as a sneak. For the passing pose, copy the upper-body angle from the contact
  so the spine does not drift.
- Game character packs (for example Kuzeaaa's Character Animation Template Pack and the itch.io platformer
  templates): crouch idle and crouch walk are separate states. One itch.io developer reported that the crouch
  frames sat lower than the other animations, so keep the crouch baseline (feet on the floor) the same as every
  other state.

## Values used (draft)
- Crouch idle
  - Leg joint height 0.88. Standing it is 1.49.
  - Pelvis tilted 22 degrees forward and chest 28 degrees, so the trunk is about 50 degrees forward.
  - Neck +16 and head +26, so she still looks ahead.
  - Front ankle 0.32 ahead of its hip joint with the foot flat.
  - Rear ankle 0.30 behind its hip joint, lifted 0.07, with the heel up about 38 degrees and the toe cap flat.
- Crouch walk
  - Cycle 0.86 s and stride 0.6, so the speed is 1.4 u/s. This is the controller's `crouchSpeed`.
  - Leg joint height 0.94, plus or minus 0.025: lowest at contact, highest at passing.
  - Each foot is planted for half the cycle. During swing it lifts 0.09, and the heel comes up at push-off.
  - The upper body keeps the crouch angles, with a small arm swing opposite to the legs.
- Legs are solved by 2-bone IK from the foot targets, so planted feet do not slide.
- The coat's lower front panel folds up (+34 degrees) so its tip stays off the floor. The long back hair is lifted
  backward a little for the same reason.
