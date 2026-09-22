using System.Collections;
using UnityEngine;

[AddComponentMenu("Shooting system/Weapon Raycast"), DisallowMultipleComponent]
public class WeaponRaycast : Weapon
{
    private bool isShoot = false;
    private bool isReload = false;

    private int AllBulletCount;

    private void Start()
    {
        AllBulletCount = weaponConfig.maxBulletCountInWeapon;
        bulletCount = weaponConfig.maxBulletCount;
    }


    public override void Shoot()
    {
        if (bulletCount > 0 && !isShoot && !isReload) StartCoroutine(ShootCoroutine());
    }
    public override void Reload()
    {
        if (AllBulletCount > 0 && bulletCount < weaponConfig.maxBulletCount && !isReload && !isShoot) StartCoroutine(RechargeCoroutine());
    }

    private IEnumerator ShootCoroutine()
    {
        isShoot = true;
        shootProgress = 0f;
        float timer = 0f;
        while (timer < weaponConfig.timeToStart)
        {
            timer += Time.deltaTime;
            shootProgress = Mathf.Clamp01(timer / weaponConfig.timeToStart);
            yield return null;
        }
        shootProgress = 1f;
        for (int i = 0; i < weaponConfig.countBulletInOneShot; i++)
        {
            Vector3 dispersion = Random.insideUnitSphere * weaponConfig.dispersion * 0.01f;
            if (Physics.Raycast(Camera.main.transform.position, Camera.main.transform.forward + dispersion, out RaycastHit hit, weaponConfig.distance))
            {
                IDamageable damageable = hit.collider.GetComponent<IDamageable>();
                if (damageable != null)
                {
                    bool isCriticalDamage = Random.value < weaponConfig.chanceCriticalDamage;

                    float crit = weaponConfig.BaseDamage * weaponConfig.criticalDamage * 0.01f; 

                    int statusUnits = (int)((float)weaponConfig.statusUnits * ammoTypeConfig.StatusMultiplier);

                    DamagePacket damagePacket = new DamagePacket(
                        ammoTypeConfig.DamageDistribution, 
                        crit, 
                        weaponConfig.chanceCriticalDamage, 
                        isCriticalDamage, 
                        statusUnits, 
                        ammoTypeConfig.ProjectileType);

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
        cooldownProgress = 0f;
        timer = 0f;
        while (timer < weaponConfig.timeCooldownShoot)
        {
            timer += Time.deltaTime;
            cooldownProgress = Mathf.Clamp01(timer / weaponConfig.timeCooldownShoot);
            yield return null;
        }
        cooldownProgress = 1f;
        isShoot = false;
    }
    private IEnumerator RechargeCoroutine()
    {
        isReload = true;
        reloadProgress = 0f;
        float timer = 0f;
        while (timer < weaponConfig.rechargeTime)
        {
            timer += Time.deltaTime;
            reloadProgress = Mathf.Clamp01(timer / weaponConfig.rechargeTime);
            yield return null;
        }
        reloadProgress = 1f;
        int availableBullet = Mathf.Clamp(weaponConfig.maxBulletCount - bulletCount, 0, AllBulletCount);
        AllBulletCount -= availableBullet;
        bulletCount += availableBullet;
        isReload = false;
    }
}
