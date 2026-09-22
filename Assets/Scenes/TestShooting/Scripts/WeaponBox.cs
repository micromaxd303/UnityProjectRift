using UnityEngine;

public class WeaponBox : MonoBehaviour
{
    [SerializeField] private WeaponConfig weaponConfig;

    private void OnTriggerEnter(Collider other)
    {
        ShootingController shootingController = other.GetComponent<ShootingController>();
        if (shootingController != null && weaponConfig != null)
        {
            shootingController.weapon.ChangeWeapon(weaponConfig);
        }
    }
}
