using System.Collections.Generic;
using UnityEngine;

public class StatusBarUI : MonoBehaviour
{
    [SerializeField]
    private List<StatusIconUI> list;

    [SerializeField]
    private StatusController controller;

    [SerializeField]
    private Color rechargeColor;

    private bool[] active;

    private void Awake()
    {
        for (int i = 0; i < list.Count; i++) list[i].gameObject.SetActive(false);
        active = new bool[list.Count];
    }

    private void Update()
    {
        if (controller == null) return;

        for (int i = 0; i < active.Length; i++) active[i] = false;
        for (int i = 0, j = 0; i < controller.statusInfo.Length; i++)
        {
            StatusController.StatusInfo status = controller.statusInfo[i];
            if (status.valueUnits > 0 || status.mode != StatusController.StatusInfo.StatusMode.Disable)
            {
                active[j] = true;
                list[j].icon.sprite = status.status.statusConfig.icon;
                switch (status.mode)
                {
                    case StatusController.StatusInfo.StatusMode.Active:
                        list[j].bar.material.SetColor("_Color", status.status.statusConfig.color);
                        //list[j].bar.material.SetFloat("_Segments", 1f);
                        list[j].bar.material.SetFloat("_Status", (status.timer / status.status.statusConfig.LifeTime) * status.maxUnits);
                        break;
                    case StatusController.StatusInfo.StatusMode.Recharge:
                        list[j].bar.material.SetColor("_Color", rechargeColor);
                        list[j].bar.material.SetFloat("_Segments", 1f);
                        list[j].bar.material.SetFloat("_Status", (1f - status.timer / status.status.statusConfig.CooldownRecharge));
                        break;
                    case StatusController.StatusInfo.StatusMode.Disable:
                        list[j].bar.material.SetColor("_Color", status.status.statusConfig.color);
                        list[j].bar.material.SetFloat("_Segments", status.maxUnits);
                        list[j].bar.material.SetFloat("_Status", (status.maxUnits - status.valueUnits) + (status.timer / status.status.statusConfig.CooldownStatusUnits));
                        break;
                }
                j++;
            }
        }
        for (int i = 0; i < active.Length; i++) list[i].gameObject.SetActive(active[i]);
    }
}
