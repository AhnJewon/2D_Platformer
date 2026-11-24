# Player hit reaction (hurt) references

Code: `Assets/Scripts/PlayerHurt.cs` (IDamageable on the player) and `PlayerController2D.Knockback`.
Face: `ArtSource/Characters/Officer/Sprites/Eye_Hurt.xcf` → `Assets/Characters/Officer/Face/Eye_Hurt.png` (Tools/make_face_files.py, synced by
update_sprites.py / watch_parts.py).

## Game feel (2D platformers)
- On a hit, knockback plus a grace period of invincibility shown by a flicker is the genre norm. Control is taken away
  during the knockback.
  - https://dev.to/aleksandrhovhannisyan/invincibility-frames-in-unity-how-to-make-a-player-flash-when-taking-damage-4nkm
  - https://construct.net/en/forum/construct-2/how-do-i-18/platformer-getting-damaged-46581
  - https://community.gamedev.tv/t/player-defeated/102404
- Grace periods run about 1–2 s; a short red tint reads as "damage taken"
  (https://babel.speldesign.uu.se/?p=827799). A flash that stays too long can read as ongoing damage
  (https://itch.io/post/10073952), so the flash is short (0.1 s real time) and light (45 %).

## Animation
- Inertia chain: the shoulders follow the chest and the head follows the shoulders with a delay, so the head peaks
  after the torso (https://ironsami-studio.gitbook.io/proceduralhitreactions/recipes-and-workflows).
- Snap back, then return to place
  (https://forums.unrealengine.com/t/simple-hit-reaction-no-documentatoin/95814). A high hit drives the head more
  (https://ssbwiki.com/flinch).
- Blend the reaction in and out as an additive layer over locomotion
  (https://dev.epicgames.com/documentation/unreal-engine/physics-driven-animation-in-unreal-engine).

## Values
- **Damage and control:** knockback 4.5 u/s away from the hit plus 3.2 up; no control for 0.28 s (the knockback slides
  out); invincible for 1.1 s with a flicker. Hit-stop 0.06 s, screen shake 0.09.
- **Pose (additive, 0.38 s):**
  - The trunk snaps 22° away from the hit (pelvis 35 %, chest 65 %) in 18 % of the time, holds, then eases back.
  - The head goes 30° (neck 40 %, head 60 %) 0.05 s later.
  - The free arm flies out 35°.
- **Direction:** a hit from the front makes her lean back; a hit from behind makes her lean forward.
- **Face:** the rig's eye (18_eye_main) is hidden for 0.5 s and Eye_Hurt is shown, rigid on
  the head bone at the normal eye's centre.
  - Art: NAI V5 + wiki preset (Yamada9000·fis mix), transparent background, seed 50101: a hurt face in profile
    (ArtSource/Characters/Officer/Source/face). The XCF layer is that image scaled to the rig eye (64 px) with its ">" tip on the
    eye's front corner; a mask keeps only the eye strokes. The generated brow is dropped and the rig's brow line stays.
  - The mouth is part of 15_head_face. Changing the mouth too needs it split into its own part (or a mouth overlay
    the same way as the eyes).
- **Debug:** H takes a hit from the front.
