using UnityEngine;

public enum DamageType : byte
{
    Normal,
    Explosive,
    Acid,
    Ice,
    Electric,
    Void
}

public readonly struct DamagePacket
{
    public readonly float[] values;
    public readonly float CriticalDamage;
    public readonly float CriticalDamageChance;
    public readonly bool isCriticalDamage;
    public readonly int StatusBuildup;
    public readonly DamageType damageType;
    public float this[DamageType t] => values[(int)t];
    public float Total 
    { 
        get
        {
            float value = 0f;
            for (int i = 0; i < values.Length; i++) value += values[i];
            value += isCriticalDamage ? CriticalDamage : 0f;
            return value;
        }
    }

    public DamagePacket(float[] values, float criticalDamage, float criticalDamageChance, bool isCriticalDamage, int statusBuildup, DamageType damageType)
    {
        this.values = new float[values.Length];
        for (int i = 0; i < values.Length; i++) this.values[i] = values[i];
        CriticalDamage = criticalDamage;
        CriticalDamageChance = criticalDamageChance;
        this.isCriticalDamage = isCriticalDamage;
        StatusBuildup = statusBuildup;
        this.damageType = damageType;
    }
    public void Multiply(float multiplier)
    {
        for (int i = 0; i < values.Length; ++i) values[i] *= multiplier;
    }
    public void ApplyResist(float[] resists)
    {
        int count = Mathf.Min(resists.Length, values.Length);
        for (int i = 0; i < count; i++)
        {
            values[i] *= (1 - resists[i]);
        }
    }
}

public struct DamageContext
{
    public readonly DamagePacket Damage;
    public GameObject Source;
    public Vector3 hitPoint;
    public Vector3 hitNormal;

    public DamageContext(DamagePacket damage)
    {
        Damage = damage;
        Source = null;
        hitPoint = Vector3.zero;
        hitNormal = Vector3.zero;
    }

    public DamageContext(DamageContext damage)
    {
        Damage = damage.Damage;
        Source = damage.Source;
        hitPoint = damage.hitPoint;
        hitNormal = damage.hitNormal;
    }
}