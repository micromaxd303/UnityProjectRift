using System.Collections;
using UnityEngine;

[AddComponentMenu("Shooting system/Weapon Raycast"), DisallowMultipleComponent]
public class WeaponRaycast : Weapon
{
    private bool isShoot = false;
    private bool isRecharge = false;

    private int bulletCount;
    private int AllBulletCount;

    private void Start()
    {
        AllBulletCount = weaponConfig.maxBulletCountInWeapon;
        bulletCount = weaponConfig.maxBulletCount;
    }


    public override void Shoot()
    {
        if (bulletCount > 0 && !isShoot) StartCoroutine(ShootCoroutine());
    }
    public override void Recharge()
    {
        if (AllBulletCount > 0 && bulletCount < weaponConfig.maxBulletCount && !isRecharge && !isShoot) StartCoroutine(RechargeCoroutine());
    }

    private IEnumerator ShootCoroutine()
    {
        isShoot = true;
        yield return new WaitForSeconds(weaponConfig.timeToStart);
        for (int i = 0; i < weaponConfig.countBulletInOneShot; i++)
        {
            if (Physics.Raycast(Camera.main.transform.position, Camera.main.transform.forward, out RaycastHit hit, weaponConfig.distance))
            {
                IDamageable damageable = hit.collider.GetComponent<IDamageable>();
                if (damageable != null)
                {
                    bool isCriticalDamage = Random.value < weaponConfig.chanceCriticalDamage;

                    float crit = weaponConfig.BaseDamage * weaponConfig.criticalDamage * 0.01f; 
                    if (weaponConfig.chanceCriticalDamage == 1f) crit = weaponConfig.BaseDamage * weaponConfig.criticalDamage * 0.01f;

                    int statusUnits = (int)((float)weaponConfig.statusUnits * ammoTypeConfig.StatusMultiplier);

                    DamagePacket damagePacket = new DamagePacket(ammoTypeConfig.DamageDistribution, isCriticalDamage ? crit : 0f, statusUnits, ammoTypeConfig.ProjectileType);
                    damagePacket.Multiply(weaponConfig.BaseDamage);

                    DamageContext damageContext = new DamageContext(damagePacket)
                    {
                        Source = hit.collider.gameObject,
                        hitPoint = hit.point,
                        hitNormal = hit.normal
                    };

                    damageable.TakeDamage(damageContext);
                }
            }
        }
        bulletCount--;
        yield return new WaitForSeconds(weaponConfig.timeCooldownShoot);
        isShoot = false;
    }
    private IEnumerator RechargeCoroutine()
    {
        isRecharge = true;
        yield return new WaitForSeconds(weaponConfig.rechargeTime);
        int availableBullet = Mathf.Clamp(weaponConfig.maxBulletCount - bulletCount, 0, AllBulletCount);
        AllBulletCount -= availableBullet;
        bulletCount += availableBullet;
        isRecharge = false;
    }
}
