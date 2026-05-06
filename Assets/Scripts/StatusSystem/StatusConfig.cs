using UnityEngine;

[CreateAssetMenu(fileName = "StatusConfig", menuName = "Scriptable Objects/StatusSystem/StatusConfig")]
public class StatusConfig : ScriptableObject
{
    [Header("Настройки статуса")]
    [Tooltip("Время жизни статуса")]
    public float LifeTime;

    [Tooltip("Cooldown перезарядки статуса для следующей активации")]
    public float CooldownRecharge;

    [Tooltip("Cooldown отката единиц статуса")]
    public float CooldownStatusUnits;

    [Tooltip("Сколько единиц статуса нужно набрать чтобы его активировать")]
    public int RequiredStatusUnits;

    [Space(5), Header("Настройки для UI")]
    [Tooltip("Цвет шкалы заполнения статуса")]
    public Color color;

    [Tooltip("Иконка статуса")]
    public Sprite icon;
}
