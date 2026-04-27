using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class ItemCardUI : MonoBehaviour
{
    [SerializeField, Tooltip("Изображение товара")]
    public Image Icon;

    [SerializeField, Tooltip("Текст стоимости товара")]
    public TMPro.TextMeshProUGUI PriceText;

    [SerializeField, Tooltip("Список объектов рамок")]
    private List<Image> Borders;

    [SerializeField, Tooltip("Объект заблокированного товара")]
    public GameObject Lock;

    [SerializeField, Tooltip("Текст с информаицей для разблокировки товара")]
    public TMPro.TextMeshProUGUI LockText;

    [HideInInspector]
    public int index;

    [HideInInspector]
    public UnityEvent<int> buttonOnClick;

    public void onClick()
    {
        buttonOnClick.Invoke(index);
    }

    public void SetBorderColor(Color color)
    {
        for (int i = 0; i < Borders.Count; i++)
        {
            if (Borders[i]) Borders[i].color = color;
        }
    }

    private void OnDestroy()
    {
        buttonOnClick.RemoveAllListeners();
    }
}
