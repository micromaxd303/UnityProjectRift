using UnityEngine;

public abstract class Status : MonoBehaviour
{
    [Tooltip("Базовый конфиг статуса")]
    public StatusConfig statusConfig;

    public IDamageable damageOutput;

    public abstract void OnEnter();
    public abstract void Tick();
    public abstract void OnExit();

    public virtual DamageContext OverrideDamage(in DamageContext context) { return context; }
}
