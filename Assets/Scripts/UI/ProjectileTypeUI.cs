using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class ProjectileTypeUI : MonoBehaviour
{
    [SerializeField] private List<Image> Borders;

    [SerializeField] private Color Selected;
    [SerializeField] private Color Disable;

    [SerializeField] private List<AmmoTypeConfig> AmmoTypes;

    private int current;

    private void Start()
    {
        for (int i = 0; i < Borders.Count; i++) Borders[i].color = Disable;
    }

    private void Update()
    {
        if (ShootingController.instance == null || ShootingController.instance.weapon == null) return;

        byte index = (byte)ShootingController.instance.weapon.ammoTypeConfig.ProjectileType;
        for (int i = 0; i < Borders.Count; i++) Borders[i].color = i == index ? Selected : Disable;

        Vector2 scroll = Input.mouseScrollDelta;
        if (scroll != Vector2.zero)
        {
            current = Mathf.Clamp(current + (int)(scroll.y * 1f), 0, AmmoTypes.Count - 1);
            ShootingController.instance.weapon.ammoTypeConfig = AmmoTypes[current];
        }
    }
}
