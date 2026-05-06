using UnityEngine;

[AddComponentMenu("Shooting system/Damage Controller")]
public class DamageController : MonoBehaviour, IDamageable
{
    [Tooltip("Массив сопротивлений к урону в порядке DamageType")]
    public float[] ResistMultiplers;

    [Tooltip("Максимальное значение HP, -1 для бессмертия")]
    public float MaxHealthPoint;

    [SerializeField, Tooltip("Компонент HP bar")]
    private HPBarUI HpBar;

    [SerializeField]
    private DamagePopupCreator DamagePopupCreator;

    [HideInInspector]
    public float HealthPoint;

    private void Awake()
    {
        HealthPoint = MaxHealthPoint;
        if (HpBar) HpBar.MaxHealthPoint = MaxHealthPoint;
    }

    public void TakeDamage(DamageContext damage)
    {
        damage.Damage.ApplyResist(ResistMultiplers);
        if (MaxHealthPoint > 0)
        {
            HealthPoint -= damage.Damage.Total;
        }
        if (HpBar) HpBar.SetValue(damage, HealthPoint);
        if (DamagePopupCreator) DamagePopupCreator.CreatePopup(damage);
        //Debug.Log($"Получен урон: {damage.Damage.Total.ToString()} от объекта {damage.Source.name}, критический урон {(damage.Damage.isCriticalDamage ? damage.Damage.CriticalDamage.ToString() : "нет")} единиц статуса: {damage.Damage.StatusBuildup.ToString()}");
    }
}