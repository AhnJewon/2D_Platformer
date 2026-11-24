# Ranged weapon (SF pistol) references

Code: `Assets/Scripts/RangedWeapon.cs` (asset `Assets/Weapons/Pistol.asset`), `Projectile.cs`,
`PlayerCombat.cs` (gun branch) and `CameraFollow2D.Shake`.

## Stance (real shooting)
- **One-handed bullseye stance:** the body is bladed (strong-side foot forward), the wrist firm or locked, and the arm
  nearly straight but not locked, so recoil is absorbed. A loose grip lets the gun move more in recoil.
  - https://outdoorempire.com/?p=11530
  - https://www.luckygunner.com/lounge/shoot-pistol-one-handed/amp/
  - https://www.ssusa.org/content/all-about-pistol-grip
- **Implemented:**
  - Shoulder-to-wrist reach is 0.72 u (the arm is about 0.75 long), so it is nearly straight.
  - The barrel sits on the aim line, with the wrist range only −12…+28° around the hand's natural line.
  - The drawn fist holds the grip with the hand roughly in line with the forearm: the grip runs about 69° from the
    bore, and the fist's hilt line is 77.4° from the hand axis, so the barrel is 8.4° above the hand axis.

## Side-view shooting animation (games)
- **SLYNYRD Pixelblog 60, "Side View Run 'N Gun":** shooting is a few upper-body frames repeated over the walk, run
  and jump legs (https://www.slynyrd.com/blog/2026/1/26/side-view-run-n-gun). Here the arm and gun pose is applied
  over whatever the Animator plays, so she can shoot while walking, backpedalling, jumping or crouching.
- **Recoil read:** the arm shortens for a frame and the muzzle rises, then both return
  (https://steamcommunity.com/app/431730/discussions/0/3274687384246030962).
  - Implemented: the muzzle kicks up 14° and the arm pulls back 0.09 u, then both ease back over 0.12 s.
  - The trunk leans back 3°.
- **Muzzle flash:** shown only for a frame or two, then particles
  (https://opengameart.org/content/gun-muzzle-flash-effects-fire-and-ion-and-melee). Implemented: a 0.06 s pop
  (scale 0.8→1.1, fade). Impacts use the same flash at half size, bursting back toward the shooter.

## Game feel
- **Vlambeer "The Art of Screenshake" (Jan Willem Nijman, 2013):** big, fast bullets, a muzzle flash, a little screen
  shake and kickback on every shot (https://gamedeveloper.com/design/squeezing-more-juice-out-of-your-game-design-).
- **Implemented:**
  - Bolt: 0.7 u long, 26 u/s.
  - Screen shake: 0.05 u for 0.08 s.
  - Kickback: the player is pushed back 0.6 u/s on the ground.

## Values (Pistol.asset)
- **Firing:** automatic (hold), 0.16 s between shots, magazine 12.
- **Reload:** 0.9 s. R reloads, and an empty magazine reloads automatically; the gun dips down and comes back.
- **Bolt:** damage 6, knockback 2.5, spread ±1.5°, range 18 u.
- **Controls:**
  - Switch weapons with 1 (sabre) / 2 (pistol), Q, the mouse wheel, or the gamepad Y/north button.
  - Fire with the left click (gamepad right trigger).

## Art (NAI V5 + wiki preset `nai-v5-yamada9000-fis-mix`, XCF in ArtSource/Characters/Officer/Sprites, background removal only)
- **Pistol:** seed 40101, a navy and gunmetal body with gold trim and a cyan energy cell.
  - About 0.55 u long. The pivot is the middle of the grip; the muzzle is at (0.46, 0.11) u.
- **Bolt:** seed 40201, a cyan energy bolt on black, with its pivot at the head.
- **Muzzle flash:** seed 40301, a cyan starburst on black, with its pivot at its origin.
- The hand is the same `Grip` drawing as the sabre.

## v2: muzzle fix and two-handed stances
- **Muzzle:** moved up to the slide's front nub, (0.46, 0.18) u; it was on the lower front face. The bolt now flies
  from the muzzle to the cursor. Before, it ran along the shoulder→cursor angle, parallel to and under the cursor line.
- **Two-handed stances** (`RangedWeapon.stance`, T cycles OneHand → Weaver → Isosceles; default Weaver):
  - Weaver: the shooting arm is extended with a slight bend (reach 0.68). The support arm bends with the elbow down
    and pulls back against the push (isometric tension), which is its recoil advantage
    (https://www.ammoland.com/2023/03/pistol-shooting-positions-weaver-vs-isosceles,
    https://www.americanrifleman.org/content/sheriff-s-tips-what-s-your-stance).
  - Isosceles: both arms are extended equally toward the target (https://www.MantisX.com/blogs/news/fundamentals-of-an-ideal-handgun-shooting-stance).
  - Support hand: the rig's far fist, IK'd to 0.03 u behind and 0.07 u under the shooting wrist (barrel frame), turned
    like the shooting fist. The far arm is drawn behind the body, so in side view mostly its forearm and fist under
    the gun show.
  - With two hands, recoil (muzzle kick, arm pull-back) and spread are ×0.55.
