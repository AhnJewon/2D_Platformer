using UnityEngine;

// A gun's look, grip and handling. PlayerCombat holds it in the near hand (HandPoses "Grip" drawing) and aims the
// barrel straight at the cursor. References (ArtSource/references/ranged_reference.md):
//   - one-handed bullseye stance: body bladed, arm nearly straight (not locked), wrist firm -> armReach ~0.72, small
//     wrist range around the hand's natural line
//   - side-view run'n'gun (SLYNYRD Pixelblog 60): the shooting pose is an upper-body layer over any leg animation;
//     recoil = the arm shortens for a frame and the muzzle rises, then returns
//   - game feel (Vlambeer "The Art of Screenshake"): big fast bullets, muzzle flash, small screen shake, kickback
[CreateAssetMenu(menuName = "Combat/Ranged Weapon")]
public class RangedWeapon : ScriptableObject
{
    public enum Stance { OneHand, Weaver, Isosceles }

    [Header("Art")]
    public Sprite sprite;            // pivot = middle of the grip
    public Sprite boltSprite;        // pivot = the bolt's head
    public Sprite muzzleFlashSprite; // pivot = the flash's origin
    public Vector2 muzzle = new Vector2(0.46f, 0.18f);   // muzzle in the sprite's space (u from the pivot, barrel = +X): the upper front nub

    [Header("Grip")]
    public string gripPose = "Grip";
    public float gripBladeOffset = 8.4f;     // barrel angle - the drawing's wrist->knuckle axis (grip ~69 deg vs 77 in the fist)
    public Vector2 gripLocal = new Vector2(0.158f, -0.019f);
    public float wristMin = -12f, wristMax = 28f;   // barrel relative to the forearm
    public float armReach = 0.72f;           // shoulder -> wrist while aiming (nearly straight)

    [Header("Stance")]
    // OneHand: bullseye (arm nearly straight). Weaver: shooting arm extended, support arm bent with the elbow down,
    // pulling back against the push (isometric tension, best recoil control). Isosceles: both arms extended equally.
    // T cycles the stance in play.
    public Stance stance = Stance.Weaver;
    public Vector2 supportGrip = new Vector2(-0.03f, -0.07f);   // support hand's wrist from the shooting wrist (barrel frame, u)
    public float twoHandRecoil = 0.55f;      // recoil and spread are multiplied by this with two hands
    public float weaverReach = 0.68f;        // shooting arm reach in the Weaver stance (a slight bend)

    [Header("Fire")]
    public bool automatic = true;            // hold to keep firing
    public float fireInterval = 0.16f;
    public int magazine = 12;
    public float reloadTime = 0.9f;
    public float damage = 6f, knockback = 2.5f;
    public float boltSpeed = 26f, range = 18f;
    public float spread = 1.5f;              // deg, random either side

    [Header("Recoil / feel")]
    public float kickAngle = 14f;            // muzzle rises this much
    public float kickBack = 0.09f;           // arm shortens this much
    public float recoilTime = 0.12f;         // back to aim
    // scale these with the gun: a pistol's recoil is taken by the wrist and arm (no body push, a tiny chest lean);
    // heavy guns (shotgun, rifle) can lean the chest more, push the player back and shake harder
    public float kickLean = 1.5f;            // chest leans back (upper body only)
    public float pushBack = 0f;              // player pushed back (u/s) - 0 for a pistol
    public float shake = 0.05f, shakeTime = 0.08f;
}
