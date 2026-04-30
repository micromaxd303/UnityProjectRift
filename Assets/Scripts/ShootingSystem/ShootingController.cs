using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ShootingController : MonoBehaviour
{
    public Weapon weapon;

    private void Update()
    {
        if (weapon)
        {
            if (Input.GetMouseButtonDown(0)) weapon.Shoot();
            else if (Input.GetKeyDown(KeyCode.R)) weapon.Recharge();
        }
    }
}
