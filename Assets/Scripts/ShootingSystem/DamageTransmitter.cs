using UnityEngine;


[AddComponentMenu("Shooting system/Damage transmiter"), DisallowMultipleComponent]
public class DamageTransmitter : MonoBehaviour, IDamageable
{
    [SerializeField, Tooltip("Множитель урона"), Range(0, 1)] 
    private float multiplier = 1f;

    [SerializeField, Tooltip("Скрипт в который нужно передать изменённый урон (скрипт должен реализовать IDamageable)")]
    private MonoBehaviour DamageableObject;

    public void TakeDamage(DamageContext context)
    {
        IDamageable damageable = DamageableObject as IDamageable;
        if (damageable != null)
        {
            DamageContext damageContext = new(context);
            damageContext.Damage.Multiply(multiplier);
            damageable.TakeDamage(damageContext);
        }
        else Debug.Log("Компонент " + DamageableObject.name + " не реализует интерфейс IDamageable");
    }
}
