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

    public override DamageContext OverrideDamage(DamageContext context)
    {
        if (isActive)
        {
            DamagePacket damagePacket = context.Damage;
            float baseDamage = 0f;
            for (int i = 0; i < damagePacket.values.Length; i++)
            {
                baseDamage += damagePacket.values[i];
            }
            DamagePacket packet = new(
                damagePacket.values,
                baseDamage * ((damagePacket.CriticalDamage / baseDamage) + CriticalDamageAdders * 0.01f),
                damagePacket.CriticalDamageChance + CriticalDamageChanceAdders,
                Random.value < (damagePacket.CriticalDamageChance + CriticalDamageChanceAdders),
                damagePacket.StatusBuildup,
                damagePacket.damageType);
            DamageContext damageContext = new DamageContext(packet)
            {
                Source = context.Source,
                hitNormal = context.hitNormal,
                hitPoint = context.hitPoint,
            };
            return damageContext;
        }
        return context;
    }
}
