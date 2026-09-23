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

public readonly struct CriticalDamage
{
    public readonly float value;
    public readonly float chance;
    public readonly bool isCritical;

    public float DamageValue => isCritical ? value : 0f;

    public CriticalDamage(float value, float chance, bool isCritical)
    {
        this.value = value;
        this.chance = chance;
        this.isCritical = isCritical;
    }
}

public readonly struct DamageValues
{
    public readonly float normalDamage;
    public readonly float explosiveDamage;
    public readonly float acidDamage;
    public readonly float iceDamage;
    public readonly float electricDamage;
    public readonly float voidDamage;

    public DamageValues(float normalDamage, float explosiveDamage, float acidDamage, float iceDamage, float electricDamage, float voidDamage)
    {
        this.normalDamage = normalDamage;
        this.explosiveDamage = explosiveDamage;
        this.acidDamage = acidDamage;
        this.iceDamage = iceDamage;
        this.electricDamage = electricDamage;
        this.voidDamage = voidDamage;
    }

    public DamageValues Multiply(float multiplier)
    {
        return new DamageValues(
            normalDamage * multiplier,
            explosiveDamage * multiplier,
            acidDamage * multiplier,
            iceDamage * multiplier,
            electricDamage * multiplier,
            voidDamage * multiplier
        );
    }
}

public readonly struct DamagePacket
{
    public readonly DamageValues damageValues;
    public readonly CriticalDamage criticalDamage;
    public readonly int statusBuildup;
    public readonly DamageType damageType;
    public float this[DamageType t] => t switch
    {
        DamageType.Normal => damageValues.normalDamage,
        DamageType.Explosive => damageValues.explosiveDamage,
        DamageType.Acid => damageValues.acidDamage,
        DamageType.Ice => damageValues.iceDamage,
        DamageType.Electric => damageValues.electricDamage,
        DamageType.Void => damageValues.voidDamage,
        _ => 0f
    };

    public float Total 
    { 
        get
        {
            float value = 0f;
            value += damageValues.normalDamage;
            value += damageValues.explosiveDamage;
            value += damageValues.acidDamage;
            value += damageValues.iceDamage;
            value += damageValues.electricDamage;
            value += damageValues.voidDamage;
            value += (value * criticalDamage.DamageValue);
            return value;
        }
    }

    public DamagePacket(DamageValues damageValues, CriticalDamage criticalDamage, DamageType damageType, int statusBuildup)
    {
        this.damageValues = damageValues;
        this.criticalDamage = criticalDamage;
        this.damageType = damageType;
        this.statusBuildup = statusBuildup;
    }
    public DamagePacket Multiply(float multiplier)
    {
        return new DamagePacket(
            damageValues.Multiply(multiplier),
            criticalDamage,
            damageType,
            statusBuildup
        );
    }
    public DamagePacket AddResist(DamageValues resistValues)
    {
        return new DamagePacket(
            new DamageValues(
                damageValues.normalDamage * Mathf.Clamp01(1 - resistValues.normalDamage),
                damageValues.explosiveDamage * Mathf.Clamp01(1 - resistValues.explosiveDamage),
                damageValues.acidDamage * Mathf.Clamp01(1 - resistValues.acidDamage),
                damageValues.iceDamage * Mathf.Clamp01(1 - resistValues.iceDamage),
                damageValues.electricDamage * Mathf.Clamp01(1 - resistValues.electricDamage),
                damageValues.voidDamage * Mathf.Clamp01(1 - resistValues.voidDamage)
            ),
            criticalDamage,
            damageType,
            statusBuildup
        );
    }
    public DamagePacket AddResist(DamageResists resists)
    {
        return new DamagePacket(
            new DamageValues(
                damageValues.normalDamage * Mathf.Clamp01(1f - resists.noramlResist),
                damageValues.explosiveDamage * Mathf.Clamp01(1f - resists.explosiveResist),
                damageValues.acidDamage * Mathf.Clamp01(1f - resists.acidResist),
                damageValues.iceDamage * Mathf.Clamp01(1f - resists.iceResist),
                damageValues.electricDamage * Mathf.Clamp01(1f - resists.electricResist),
                damageValues.voidDamage * Mathf.Clamp01(1f - resists.voidResist)
                ),
            criticalDamage,
            damageType,
            statusBuildup
            );
    }
}


public readonly struct DamageContext
{
    public readonly DamagePacket Damage;
    public readonly GameObject Source;
    public readonly Vector3 hitPoint;
    public readonly Vector3 hitNormal;

    public DamageContext(DamagePacket damage)
    {
        Damage = damage;
        Source = null;
        hitPoint = Vector3.zero;
        hitNormal = Vector3.zero;
    }

    public DamageContext(DamageContext context)
    {
        Damage = context.Damage;
        Source = context.Source;
        hitPoint = context.hitPoint;
        hitNormal = context.hitNormal;
    }

    public DamageContext(DamagePacket damage, GameObject source, Vector3 hitPoint, Vector3 hitNormal)
    {
        Damage = damage;
        Source = source;
        this.hitPoint = hitPoint;
        this.hitNormal = hitNormal;
    }
}

[System.Serializable]
public class DamageResists 
{
    [Header("Resists")]
    [Range(0, 1)] public float noramlResist = 0f;
    [Range(0, 1)] public float explosiveResist = 0f;
    [Range(0, 1)] public float acidResist = 0f;
    [Range(0, 1)] public float iceResist = 0f;
    [Range(0, 1)] public float electricResist = 0f;
    [Range(0, 1)] public float voidResist = 0f;

    public DamageResists(DamageResists resists)
    {
        noramlResist = resists.noramlResist;
        explosiveResist = resists.explosiveResist;
        acidResist = resists.acidResist;
        iceResist = resists.iceResist;
        electricResist = resists.electricResist;
        voidResist = resists.voidResist;
    }

    public DamageResists(float noramlResist, float explosiveResist, float acidResist, float iceResist, float electricResist, float voidResist)
    {
        this.noramlResist = Mathf.Clamp01(noramlResist);
        this.explosiveResist = Mathf.Clamp01(explosiveResist);
        this.acidResist = Mathf.Clamp01(acidResist);
        this.iceResist = Mathf.Clamp01(iceResist);
        this.electricResist = Mathf.Clamp01(electricResist);
        this.voidResist = Mathf.Clamp01(voidResist);
    }

    public static DamageResists operator + (DamageResists a, DamageResists b)
    {
        return new DamageResists(
            new DamageResists(
                Mathf.Clamp01(a.noramlResist + b.noramlResist),
                Mathf.Clamp01(a.explosiveResist + b.explosiveResist),
                Mathf.Clamp01(a.acidResist + b.acidResist),
                Mathf.Clamp01(a.iceResist + b.iceResist),
                Mathf.Clamp01(a.electricResist + b.electricResist),
                Mathf.Clamp01(a.voidResist + b.voidResist)
            )
        );
    }
    public static DamageResists operator -(DamageResists a, DamageResists b)
    {
        return new DamageResists(
            new DamageResists(
                Mathf.Clamp01(a.noramlResist - b.noramlResist),
                Mathf.Clamp01(a.explosiveResist - b.explosiveResist),
                Mathf.Clamp01(a.acidResist - b.acidResist),
                Mathf.Clamp01(a.iceResist - b.iceResist),
                Mathf.Clamp01(a.electricResist - b.electricResist),
                Mathf.Clamp01(a.voidResist - b.voidResist)
            )
        );
    }
}