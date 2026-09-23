using UnityEngine;
using System.Collections.Generic;


[AddComponentMenu("Shooting system/Damage transmiter"), DisallowMultipleComponent]
public class DamageTransmitter : MonoBehaviour, IDamageable
{
    [SerializeField, Tooltip("Множитель урона")] 
    private float multiplier = 1f;

    [SerializeField, Tooltip("Список компонентов в которые нужно передать изменённый урон (скрипт должен реализовать IDamageable)")]
    private List<MonoBehaviour> DamageableObjects;

    public void TakeDamage(in DamageContext context)
    {
        DamageContext damageContext = 
            new DamageContext(
                context.Damage.Multiply(multiplier), context.Source, context.hitPoint, context.hitNormal
            );

        for (int i = 0; i < DamageableObjects.Count; ++i)
        {
            MonoBehaviour component = DamageableObjects[i];
            if (component == null)
            {
#if UNITY_EDITOR
                Debug.LogError("Компонент равен null");
#endif
                continue;
            }
            if (component is IDamageable damageable)
            {
                damageable.TakeDamage(damageContext);
            }
#if UNITY_EDITOR
            else Debug.LogError($"Компонент {component.name} объекта {component.gameObject.name} не реализует интерфейс IDamageable");
#endif
        }
    }
}
