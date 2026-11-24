using UnityEngine;
using static CombatMath;

// Gun behaviour (RangedWeapon): the arm holds the gun straight out at the cursor; click fires (hold = automatic),
// R reloads, an empty magazine shows red for a beat then reloads by itself, T cycles one hand / Weaver / Isosceles.
// Recoil stays in the wrist, arm and chest (the hips and legs do not move); heavy guns can add pushBack.
// The energy-cell ammo gauge (GunCellGauge) is created on the weapon sprite when the data asks for it.
public class GunBehaviour : WeaponBehaviour
{
    readonly RangedWeapon g;
    int ammo;
    float nextShot, lastShot = -10f, reloadStart = -10f, reloadEnd = -10f;
    bool reloadPending, reloadFromEmpty;
    RangedWeapon.Stance? stanceOverride;
    GunCellGauge gauge;
    public int ShotCount { get; private set; }

    public GunBehaviour(PlayerCombat owner, RangedWeapon data) : base(owner, data) { g = data; ammo = data.magazine; }

    public RangedWeapon.Stance Stance => stanceOverride ?? g.stance;
    bool TwoHanded => Stance != RangedWeapon.Stance.OneHand;
    float RecoilScale => TwoHanded ? g.twoHandRecoil : 1f;
    public bool Reloading => reloadPending && Time.time < reloadEnd;

    public override int Ammo => ammo;
    public override int Magazine => g.magazine;
    public override float ReloadProgress => reloadPending ? Mathf.Clamp01((Time.time - reloadStart) / Mathf.Max(g.reloadTime, 1e-3f)) : -1f;
    public override float SinceShot => Time.time - lastShot;
    public override string StateName => Reloading ? "Reload" : ammo <= 0 ? "Empty" : "Ready";

    public void SetAmmoForTest(int n) { ammo = n; reloadPending = false; reloadEnd = -10f; }
    public override void AltAction() => stanceOverride = (RangedWeapon.Stance)(((int)Stance + 1) % 3);

    public override void OnEquip()
    {
        if (gauge == null && g.cellGauge && g.sprite != null)
        {
            gauge = owner.WeaponTransform.gameObject.AddComponent<GunCellGauge>();
            gauge.Init(owner.WeaponRenderer, g.sprite, new[] { g.vent, g.muzzle });
        }
    }
    public override void OnUnequip() { if (gauge != null) gauge.Release(null); }
    public override void OnRemoved() { if (gauge != null) { Object.Destroy(gauge); gauge = null; } }

    public override void Reload()
    {
        if (reloadPending || ammo >= g.magazine) return;
        reloadStart = Time.time; reloadEnd = Time.time + g.reloadTime; reloadPending = true; reloadFromEmpty = ammo <= 0;
    }

    public override void Tick(bool pressed, bool held, ref bool queued)
    {
        if (reloadPending && Time.time >= reloadEnd) { ammo = g.magazine; reloadPending = false; }
        if (ammo <= 0 && !reloadPending && Time.time - lastShot >= g.emptyReloadDelay) Reload();
        bool want = queued || pressed || g.automatic && held;
        bool sliding = owner.Controller.IsSliding;
        if (want && Time.time >= nextShot && !sliding && !Reloading)
        {
            queued = false;
            if (ammo > 0) Fire(); else if (Time.time - lastShot >= g.emptyReloadDelay) Reload();
        }
        else if (!want || sliding) queued = false;
    }

    float recoil, reload;
    public override WeaponPose Pose()
    {
        // recoil: snaps up on the shot and eases back; reload: the gun dips down and comes back up
        float rt = (Time.time - lastShot) / Mathf.Max(g.recoilTime, 1e-3f);
        recoil = rt < 1f ? Mathf.Pow(1f - rt, 2f) : 0f;
        reload = Reloading ? Mathf.Sin(Mathf.Clamp01(ReloadProgress) * Mathf.PI) : 0f;
        float reach = Stance == RangedWeapon.Stance.Weaver ? g.weaverReach : g.armReach;
        return new WeaponPose
        {
            rel = g.kickAngle * RecoilScale * recoil - 50f * reload,
            reach = reach - g.kickBack * RecoilScale * recoil - 0.15f * reload,
            armOnWeapon = true,
            lean = g.kickLean * recoil, leanFromChest = true,
        };
    }

    public override void AfterPose(float weaponAngle)
    {
        if (TwoHanded)
        {
            Vector2 off = g.supportGrip;
            if (Stance == RangedWeapon.Stance.Isosceles) off += new Vector2(0.02f, 0.01f);
            owner.SupportArm(weaponAngle, off);
        }
        if (gauge != null) gauge.Show(ammo, g.magazine, ReloadProgress, reloadFromEmpty, true);
    }

    void Fire()
    {
        ammo--; ShotCount++;
        lastShot = Time.time; nextShot = Time.time + g.fireInterval;
        var weapon = owner.WeaponTransform;
        Vector2 muzzle = weapon.TransformPoint(new Vector3(g.muzzle.x, g.muzzle.y, 0f));
        Vector2 barrel = ((Vector2)weapon.TransformVector(Vector3.right)).normalized;
        // from the MUZZLE to the cursor (the aim angle is measured from the shoulder: shooting along it would run
        // parallel to the cursor line, under or over it); a cursor right at the gun falls back to the barrel
        Vector2 toCursor = owner.AimWorld - muzzle;
        Vector2 aim = toCursor.magnitude > 0.8f && Vector2.Dot(toCursor, barrel) > 0f ? toCursor.normalized : barrel;
        Vector2 dir = Vector2.Lerp(aim, barrel, 0.15f).normalized;
        float spread = g.spread * RecoilScale;
        dir = (Vector2)(Quaternion.Euler(0, 0, Random.Range(-spread, spread)) * dir);
        Projectile.Spawn(g.boltSprite, g.muzzleFlashSprite, muzzle, dir, g.boltSpeed, g.damage, g.knockback, g.range, owner.gameObject);
        if (g.muzzleFlashSprite != null)
        {
            var f = new GameObject("MuzzleFlash");
            f.transform.SetParent(weapon, false);
            f.transform.localPosition = new Vector3(g.muzzle.x, g.muzzle.y, -0.01f);
            var sr = f.AddComponent<SpriteRenderer>(); sr.sprite = g.muzzleFlashSprite; sr.sortingOrder = 42;
            f.AddComponent<FlashFade>().Init(0.06f, 0.8f, 1.1f);
        }
        CameraFollow2D.Shake(g.shake, g.shakeTime);
        var rb = owner.Body;
        if (g.pushBack > 0f && owner.Controller.IsGrounded && rb != null) { rb.linearVelocity -= new Vector2(dir.x, 0f) * g.pushBack; owner.Controller.MarkStepInPlace(); }
    }
}
