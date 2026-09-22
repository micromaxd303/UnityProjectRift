using UnityEngine;

[CreateAssetMenu(fileName = "AmmoTypeConfig", menuName = "Scriptable Objects/Shooting system/AmmoTypeConfig")]
public class AmmoTypeConfig : ScriptableObject
{
    [Tooltip("Тип пули")]
    public DamageType ProjectileType;

    [Tooltip("Коэффициенты распределения урона (сумма должна быть равна 1)")]
    public float[] DamageDistribution;

    [Tooltip("Множитель единиц статуса")]
    public float StatusMultiplier = 1f;
}
