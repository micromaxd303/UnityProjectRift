using Unity.VisualScripting;
using UnityEngine;

public class WeaponInfoUI : MonoBehaviour
{
    [SerializeField] private ShootingController shootingController;

    [SerializeField] private TMPro.TextMeshProUGUI weaponNameText;
    [SerializeField] private TMPro.TextMeshProUGUI weaponBulletCountText;
    [SerializeField] private UnityEngine.UI.Image weaponBulletCountBar;
    [SerializeField] private TMPro.TextMeshProUGUI weaponDamageText;
    [SerializeField] private TMPro.TextMeshProUGUI weaponCriticalDamageText;
    [SerializeField] private TMPro.TextMeshProUGUI weaponChanceCriticalDamageText;
    [SerializeField] private TMPro.TextMeshProUGUI weaponStatusUnitsText;
    [SerializeField] private TMPro.TextMeshProUGUI weaponAutoShootingText;

    [SerializeField] private UnityEngine.UI.Image weaponReloadBar;
    [SerializeField] private UnityEngine.UI.Image weaponShootBar;
    [SerializeField] private UnityEngine.UI.Image weaponCooldownBar;

    private void Update()
    {
        if (shootingController != null && shootingController.weapon != null)
        {
            weaponNameText.text = shootingController.weapon.weaponConfig.name;
            weaponBulletCountText.text = shootingController.weapon.bulletCount.ToString() + "/" + shootingController.weapon.weaponConfig.maxBulletCount.ToString();
            weaponBulletCountBar.fillAmount = (float)shootingController.weapon.bulletCount / shootingController.weapon.weaponConfig.maxBulletCount;
            weaponDamageText.text = shootingController.weapon.weaponConfig.BaseDamage.ToString();
            weaponCriticalDamageText.text = shootingController.weapon.weaponConfig.criticalDamage.ToString();
            weaponChanceCriticalDamageText.text = shootingController.weapon.weaponConfig.chanceCriticalDamage.ToString();
            weaponStatusUnitsText.text = shootingController.weapon.weaponConfig.statusUnits.ToString();
            weaponAutoShootingText.text = shootingController.weapon.weaponConfig.AutoShooting ? "Yes" : "No";

            weaponReloadBar.fillAmount = shootingController.weapon.reloadProgress;
            weaponShootBar.fillAmount = shootingController.weapon.shootProgress;
            weaponCooldownBar.fillAmount = shootingController.weapon.cooldownProgress;
        }
    }
}
