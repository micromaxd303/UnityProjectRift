using System;
using UnityEngine;

public abstract class Weapon : MonoBehaviour
{
    public AmmoTypeConfig ammoTypeConfig;
    public WeaponConfig weaponConfig;

    [Tooltip("Откуда и куда летит луч выстрела. Укажи объект CinemachineCamera: он не трясётся от анимаций камеры. Пусто — Camera.main.")]
    public Transform aimOrigin;

    [HideInInspector] public int bulletCount;

    [HideInInspector] public float reloadProgress = 1f;
    [HideInInspector] public float cooldownProgress = 1f;
    [HideInInspector] public float shootProgress = 1f;

    /// <summary>Произошёл выстрел (после задержки timeToStart).</summary>
    public event Action<Weapon> Shot;
    /// <summary>Началась перезарядка.</summary>
    public event Action<Weapon> ReloadStarted;
    /// <summary>Перезарядка закончилась.</summary>
    public event Action<Weapon> ReloadFinished;

    protected Transform AimTransform => aimOrigin != null ? aimOrigin : Camera.main.transform;

    protected void RaiseShot() => Shot?.Invoke(this);
    protected void RaiseReloadStarted() => ReloadStarted?.Invoke(this);
    protected void RaiseReloadFinished() => ReloadFinished?.Invoke(this);

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
