using UnityEngine;

[CreateAssetMenu(fileName = "WeaponConfig", menuName = "Scriptable Objects/Shooting system/WeaponConfig")]
public class WeaponConfig : ScriptableObject
{
    [Tooltip("Название оружия")]
    public new string name;

    [Tooltip("Описание оружия")]
    public string description;

    [Tooltip("Базовый урон оружия")]
    public float BaseDamage;

    [Tooltip("Точность")]
    public float accuracy;

    [Tooltip("Максимальная дальность стрельбы")]
    public float distance;

    [Tooltip("Критический урон (в %)")]
    public float criticalDamage;

    [Tooltip("Шанс критического урона"), Range(0f, 1f)]
    public float chanceCriticalDamage;

    [Tooltip("Количество единиц статуса за выстрел")]
    public int statusUnits;

    [Tooltip("Количество пуль за один выстрел")]
    public int countBulletInOneShot;

    [Tooltip("Время до начала стрельбы (в секундах)")]
    public float timeToStart;

    [Tooltip("Время до следующего выстрела")]
    public float timeCooldownShoot;

    [Tooltip("Время перезарядки")]
    public float rechargeTime;

    [Tooltip("Максимальное количество патронов в оружии в целом")]
    public int maxBulletCountInWeapon;

    [Tooltip("Максимальное количество заряженных патронов")]
    public int maxBulletCount;

    [Tooltip("Авто стрельба по удержании кнопки")]
    public bool AutoShooting;
}
