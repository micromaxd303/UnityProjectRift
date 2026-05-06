using UnityEngine;
using UnityEngine.UI;

[AddComponentMenu("UI/Bars/HP bar")]
public class HPBarUI : MonoBehaviour
{
    [SerializeField]
    private Image HpBar;

    [SerializeField]
    private Image DamageBar;

    [SerializeField]
    private TMPro.TextMeshProUGUI HpText;

    [SerializeField, Tooltip("Кривая заполнения полосы урона")]
    private AnimationCurve DamageBarFillCurve;

    [SerializeField, Tooltip("Кривая прозрачности полосы урона")]
    private AnimationCurve DamageBarAlphaCurve;

    [HideInInspector]
    public float MaxHealthPoint;

    private float Timer = 0f;

    private void Start()
    {
        if (HpText) HpText.text = MaxHealthPoint.ToString();
    }

    public void SetValue(DamageContext damage, float healthPoint)
    {
        if (HpBar) HpBar.fillAmount = healthPoint / MaxHealthPoint;
        if (HpText) HpText.text = healthPoint.ToString();
        if (DamageBar)
        {
            DamageBar.rectTransform.sizeDelta = new Vector2((damage.Damage.Total / MaxHealthPoint) * HpBar.rectTransform.rect.width, DamageBar.rectTransform.sizeDelta.y);
            Vector3 DamageBarPosition = DamageBar.rectTransform.localPosition;
            DamageBarPosition.x = Mathf.Lerp(HpBar.rectTransform.rect.xMin, HpBar.rectTransform.rect.xMax, HpBar.fillAmount);
            DamageBar.rectTransform.localPosition = DamageBarPosition;
            Timer = 0f;
        }
    }

    private void Update()
    {
        Timer += Time.deltaTime;
        if (DamageBar)
        {
            DamageBar.fillAmount = DamageBarFillCurve.Evaluate(Timer);
            Color color = DamageBar.color;
            color.a = DamageBarAlphaCurve.Evaluate(Timer);
            DamageBar.color = color;
        }
    }
}
