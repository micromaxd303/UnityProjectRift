using UnityEngine;
using System.Collections.Generic;


[AddComponentMenu("Shooting system/Damage transmiter"), DisallowMultipleComponent]
public class DamageTransmitter : MonoBehaviour, IDamageable
{
    [SerializeField, Tooltip("Множитель урона"), Range(0, 1)] 
    private float multiplier = 1f;

    [SerializeField, Tooltip("Список компонентов в которые нужно передать изменённый урон (скрипт должен реализовать IDamageable)")]
    private List<MonoBehaviour> DamageableObjects;

    public void TakeDamage(DamageContext context)
    {
        for (int i = 0; i < DamageableObjects.Count; ++i)
        {
            if (DamageableObjects[i] == null)
            {
#if UNITY_EDITOR
                Debug.Log("Компонент равен null");
#endif
                continue;
            }
            IDamageable damageable = DamageableObjects[i] as IDamageable;
            if (damageable != null)
            {
                DamageContext damageContext = new(context);
                damageContext.Damage.Multiply(multiplier);
                damageable.TakeDamage(damageContext);
            }
#if UNITY_EDITOR
            else Debug.Log("Компонент " + DamageableObjects[i].name + " не реализует интерфейс IDamageable");
#endif
        }
    }
}
