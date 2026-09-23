using UnityEngine;

[AddComponentMenu("Status System/Status Void")]
public class StatusVoid : Status
{
    [Tooltip("Добавка к критическому урону (в %)")]
    public float CriticalDamageAdders;

    [Tooltip("Добавка к шансу кричесекого урона")]
    public float CriticalDamageChanceAdders;

    private bool isActive = false;

    public override void OnEnter()
    {
        isActive = true;
    }

    public override void Tick()
    {

    }

    public override void OnExit()
    {
        isActive = false;
    }

    public override DamageContext OverrideDamage(in DamageContext context)
    {
        if (isActive)
        {
            DamageContext damageContext = new DamageContext(
                new DamagePacket(
                    context.Damage.damageValues,
                    new CriticalDamage(
                        context.Damage.criticalDamage.value + CriticalDamageAdders,
                        Mathf.Clamp01(context.Damage.criticalDamage.chance + CriticalDamageChanceAdders),
                        Random.value < (context.Damage.criticalDamage.chance + CriticalDamageChanceAdders)
                        ),
                    context.Damage.damageType,
                    context.Damage.statusBuildup
                ),
                context.Source,
                context.hitPoint,
                context.hitNormal
                );
            return damageContext;
        }
        return context;
    }
}
