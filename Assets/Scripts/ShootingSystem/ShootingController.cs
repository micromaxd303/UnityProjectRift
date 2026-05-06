using UnityEngine;

[AddComponentMenu("Shooting system/Shooting controller")]
public class ShootingController : MonoBehaviour
{
    public Weapon weapon;

    public static ShootingController instance;

    private void Awake()
    {
        instance = this;
    }

    private void Update()
    {
        if (weapon)
        {
            if (weapon.weaponConfig.AutoShooting)
            {
                if (Input.GetMouseButton(0)) weapon.Shoot();
            }
            else if (Input.GetMouseButtonDown(0)) weapon.Shoot();
        }
        else if (Input.GetKeyDown(KeyCode.R)) weapon.Recharge();
    }
}
