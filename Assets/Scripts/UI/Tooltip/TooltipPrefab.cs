using UnityEngine;
using UnityEngine.UI;

public class TooltipPrefab : MonoBehaviour
{
    [SerializeField]
    private TMPro.TextMeshProUGUI textName;

    [SerializeField]
    private TMPro.TextMeshProUGUI textDescription;

    [SerializeField]
    private Image border;

    public void SetActive(bool active)
    {
        gameObject.SetActive(active);
    }

    public void SetInfo(TooltipUIData data)
    {
        if (textName) textName.text = data.name;
        if (textDescription) textDescription.text = data.description;
        if (data.overrideColor && border) border.color = data.color;
    }
}
