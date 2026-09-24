using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class ProjectileTypeUI : MonoBehaviour
{
    [SerializeField] private List<Image> Borders;

    [SerializeField] private Color Selected;
    [SerializeField] private Color Disable;

    [SerializeField] private List<AmmoTypeConfig> AmmoTypes;
    [SerializeField] private ShootingController shootingController;

    private int current;

    private void Start()
    {
        for (int i = 0; i < Borders.Count; i++) Borders[i].color = Disable;
    }

    private void Update()
    {
        if (shootingController == null || shootingController.weapon == null) return;

        byte index = (byte)shootingController.weapon.ammoTypeConfig.projectileType;
        for (int i = 0; i < Borders.Count; i++) Borders[i].color = i == index ? Selected : Disable;

        Vector2 scroll = Input.mouseScrollDelta;
        if (scroll != Vector2.zero)
        {
            current = Mathf.Clamp(current - (int)(scroll.y * 1f), 0, AmmoTypes.Count - 1);
            shootingController.weapon.ammoTypeConfig = AmmoTypes[current];
        }
    }
}
