using UnityEngine;

[AddComponentMenu("Status System/Status Infection")]
public class StatusInfection : Status
{
    [Tooltip("Cooldown нанесения урона")]
    public float CooldownTime = 1f;

    [Tooltip("Наносимый урон: процент от максимального HP врага"), Range(0f, 1f)]
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
                DamagePacket packet = new DamagePacket(
                    new DamageValues(ProcentDamage * MaxHealthPoint, 0f, 0f, 0f, 0f, 0f),
                    new CriticalDamage(0f, 0f, false),
                    DamageType.Normal, 
                    0
                    );
                DamageContext damage = new DamageContext(
                    packet,
                    gameObject,
                    Vector3.zero,
                    Vector3.zero
                    );
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
