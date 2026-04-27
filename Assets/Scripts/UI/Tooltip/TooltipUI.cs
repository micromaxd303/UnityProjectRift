using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

public class TooltipUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public TooltipUIData data;

    [Tooltip("Показывать Tooltip мгновенно, не нужно держать курсор N времени для появления")]
    public bool showQuickly = false;

    private bool isOnPointer = false;

    public void OnPointerEnter(PointerEventData data)
    {
        isOnPointer = true;
        if (showQuickly) TooltipSystem.instance.Show(this.data);
        else StartCoroutine(timerCoroutine());
    }

    public void OnPointerExit(PointerEventData data)
    {
        isOnPointer = false;
        TooltipSystem.instance.Hide();
    }

    private IEnumerator timerCoroutine()
    {
        yield return new WaitForSeconds(TooltipSystem.instance.timer);
        if (isOnPointer) TooltipSystem.instance.Show(this.data);
    }
}

[System.Serializable]
public class TooltipUIData
{
    [Tooltip("Заголовок")]
    public string name;

    [Tooltip("Описание")]
    public string description;

    [Tooltip("Переопределить цвет выделения")]
    public bool overrideColor = false;

    [Tooltip("Цвет выделения")]
    public Color color = Color.white;
}
