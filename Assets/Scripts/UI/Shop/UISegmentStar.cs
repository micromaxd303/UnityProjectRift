using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UISegmentStar : MonoBehaviour
{
    [SerializeField] private Image Background;
    [SerializeField] private Image Icon;
    [SerializeField] private TextMeshProUGUI Text;

    public void SetInfo(UIPropertyStarCreator.UISegmentStarInfo info)
    {
        Text.text = info.Text;
        Icon.sprite = info.Icon;
        Icon.color = info.IconColor;
        Background.color = info.BackgroundColor;
    }
}
