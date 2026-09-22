using UnityEngine;

public abstract class Weapon : MonoBehaviour
{
    public AmmoTypeConfig ammoTypeConfig;
    public WeaponConfig weaponConfig;

    public int bulletCount;

    public float reloadProgress = 1f;
    public float cooldownProgress = 1f;
    public float shootProgress = 1f;

    public void ChangeWeapon(WeaponConfig newWeaponConfig)
    {
        weaponConfig = newWeaponConfig;
        bulletCount = weaponConfig.maxBulletCount;
        reloadProgress = 1f;
        cooldownProgress = 1f;
        shootProgress = 1f;
    }

    public abstract void Shoot();
    public abstract void Reload();
}
