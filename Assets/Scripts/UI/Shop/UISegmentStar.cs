using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UISegmentStar : MonoBehaviour
{
    [SerializeField, Tooltip("Задний фон сегмента")] 
    private Image Background;

    [SerializeField, Tooltip("Рамка выделения")] 
    private Image Border;

    [SerializeField, Tooltip("Иконка свойства")] 
    private Image Icon;

    [SerializeField, Tooltip("Текст значения свойства")] 
    private TextMeshProUGUI Text;

    [SerializeField, Tooltip("Система Tooltip для свойства")] 
    private TooltipUI Tooltip;

    public void SetInfo(UISegmentStarInfo info)
    {
        if (Text)
        {
            Text.text = info.value;

        }
        if (Icon)
        {
            if (info.Icon != null) Icon.sprite = info.Icon;
            Icon.color = info.IconColor;
        }
        if (Background) Background.color = info.BackgroundColor;
        if (Border) Border.color = info.BorderColor;
        if (Tooltip)
        {
            Tooltip.data.name = info.name;
            Tooltip.data.description = info.description;
        }
    }

    public void TextRotate(Vector3 angle)
    {
        if (Text) Text.transform.Rotate(angle);
        if (Icon) Icon.transform.Rotate(angle);
    }
}
