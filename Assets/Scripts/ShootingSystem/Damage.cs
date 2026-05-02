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
    private readonly float[] values;
    public readonly float CriticalDamage;
    public readonly int StatusBuildup;
    public readonly DamageType damageType;
    public float this[DamageType t] => values[(int)t];
    public float Total 
    { 
        get
        {
            float value = 0f;
            for (int i = 0; i < values.Length; i++) value += values[i];
            return value;
        }
    }

    public DamagePacket(float[] values, float criticalDamage, int statusBuildup, DamageType damageType)
    {
        this.values = new float[values.Length];
        for (int i = 0; i < values.Length; i++) this.values[i] = values[i];
        CriticalDamage = criticalDamage;
        StatusBuildup = statusBuildup;
        this.damageType = damageType;
    }
    public void Multiply(float multiplier)
    {
        for (int i = 0; i < values.Length; ++i) values[i] *= multiplier;
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