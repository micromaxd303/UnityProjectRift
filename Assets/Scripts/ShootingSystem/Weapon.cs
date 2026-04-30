using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class Weapon : MonoBehaviour
{
    public AmmoTypeConfig ammoTypeConfig;
    public WeaponConfig weaponConfig;

    public abstract void Shoot();
    public abstract void Recharge();
}
