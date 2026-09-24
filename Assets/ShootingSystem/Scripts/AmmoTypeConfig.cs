using UnityEngine;

[CreateAssetMenu(fileName = "AmmoTypeConfig", menuName = "Scriptable Objects/Shooting system/AmmoTypeConfig")]
public class AmmoTypeConfig : ScriptableObject
{
    [Tooltip("Тип пули")]
    public DamageType projectileType;

    [Header("Распределение урона")]
    [SerializeField, Range(0f, 1f)] private float normalDamage;
    [SerializeField, Range(0f, 1f)] private float explosiveDamage;
    [SerializeField, Range(0f, 1f)] private float acidDamage;
    [SerializeField, Range(0f, 1f)] private float iceDamage;
    [SerializeField, Range(0f, 1f)] private float electricDamage;
    [SerializeField, Range(0f, 1f)] private float voidDamage;

    [Tooltip("Множитель единиц статуса")]
    public float statusMultiplier = 1f;

    public DamageValues damageValues;

    private void OnEnable()
    {
        damageValues = new DamageValues(
            normalDamage, 
            explosiveDamage, 
            acidDamage, 
            iceDamage, 
            electricDamage, 
            voidDamage
            );
    }
}
