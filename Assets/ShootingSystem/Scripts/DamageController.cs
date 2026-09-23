using UnityEngine;

[AddComponentMenu("Shooting system/Damage Controller")]
public class DamageController : MonoBehaviour, IDamageable
{
    public DamageResists resists;

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

    public void TakeDamage(in DamageContext damage)
    {
        DamageContext newDamage = new DamageContext(
            damage.Damage.AddResist(resists), 
            damage.Source, 
            damage.hitPoint, 
            damage.hitNormal
            );
        if (MaxHealthPoint > 0)
        {
            HealthPoint -= newDamage.Damage.Total;
        }
        if (HpBar) HpBar.SetValue(newDamage, HealthPoint);
        if (DamagePopupCreator) DamagePopupCreator.CreatePopup(newDamage);
    }
}