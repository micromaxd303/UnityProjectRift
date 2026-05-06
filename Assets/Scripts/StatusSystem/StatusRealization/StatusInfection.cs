using UnityEngine;

[AddComponentMenu("Status System/Status Infection")]
public class StatusInfection : Status
{
    [Tooltip("Cooldown нанесения урона")]
    public float CooldownTime = 1f;

    [Tooltip("Наносимый урон: процент от максимального HP врага (от 0 до 1)")]
    public float ProcentDamage = 0.1f;

    [SerializeField]
    private DamageController controller;
    private float MaxHealthPoint = 0f;

    private float Timer = 0f;

    private void Awake()
    {
        if (controller) MaxHealthPoint = controller.MaxHealthPoint;
    }

    public override void OnEnter()
    {
        Timer = 0f;
    }

    public override void Tick()
    {
        Timer += Time.deltaTime;
        if (Timer >= CooldownTime)
        {
            if (damageOutput != null)
            {
                DamagePacket packet = new DamagePacket(new float[] { MaxHealthPoint * ProcentDamage, 0f, 0f, 0f, 0f, 0f}, 0f, 0f, false, 0, DamageType.Normal);
                DamageContext damage = new DamageContext(packet);
                damage.Source = gameObject;
                damageOutput.TakeDamage(damage);
            }
            Timer = 0f;
        }
    }

    public override void OnExit()
    {
        Timer = 0f;
    }
}
