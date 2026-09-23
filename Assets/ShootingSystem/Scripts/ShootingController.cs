using UnityEngine;

[AddComponentMenu("Shooting system/Shooting controller")]
public class ShootingController : MonoBehaviour
{
    public Weapon weapon;

    private void Update()
    {
        if (weapon)
        {
            if (weapon.weaponConfig.autoShooting)
            {
                if (GameServices.Input.Combat.Fire.Held) weapon.Shoot();
            }
            else if (GameServices.Input.Combat.Fire.Pressed) weapon.Shoot();

            if (GameServices.Input.Combat.Reload.Pressed) weapon.Reload();
        }
    }
}
