using System.Collections.Generic;
using UnityEngine;

[AddComponentMenu("Status System/Status Controller")]
public class StatusController : MonoBehaviour, IDamageable
{
    [SerializeField, Tooltip("Список статусов в порядке enum DamageType")]
    private List<Status> statusList;

    [SerializeField, Tooltip("Скрипт в который будет передан измененный урон (скрипт должен реализовать IDamageable)")]
    private MonoBehaviour scriptDamgeOutput;


    private IDamageable damageOutput;
    public StatusInfo[] statusInfo {  get; private set; }

    private void Awake()
    {
        statusInfo = new StatusInfo[Mathf.Min(System.Enum.GetValues(typeof(DamageType)).Length, statusList.Count)];
        for (int i = 0; i < statusInfo.Length; i++)
        {
            statusInfo[i] = new StatusInfo(statusList[i]);
        }

        if (scriptDamgeOutput != null) 
        { 
            damageOutput = scriptDamgeOutput as IDamageable;
            if (damageOutput != null)
            {
                for (int i = 0; i < statusInfo.Length; i++) if (statusList[i]) statusList[i].damageOutput = damageOutput;
            }
            else Debug.LogError($"Компонент {scriptDamgeOutput.name} не реализует интерфейс IDamageable");
        }
    }

    public void TakeDamage(DamageContext context)
    {
        int index = (byte)context.Damage.damageType;
        if (index >= statusInfo.Length) return;
        statusInfo[index].AddUnits(context.Damage.StatusBuildup);
        for (int i = 0; i < statusInfo.Length; i++)
        {
            if (statusInfo[i].mode == StatusInfo.StatusMode.Active)
            {
                context = statusInfo[i].status.OverrideDamage(context);
            }
        }
        if (damageOutput != null) damageOutput.TakeDamage(context);
    }

    private void Update()
    {
        for (int i = 0; i < statusInfo.Length; i++)
        {
            statusInfo[i].Update();
        }
    }

    public class StatusInfo
    {
        public int valueUnits = 0;
        public int maxUnits;
        public StatusMode mode = StatusMode.Disable;

        public float timer { get; private set; }
        public readonly Status status;

        public StatusInfo(Status status) 
        { 
            this.status = status;
            if (status && status.statusConfig) maxUnits = status.statusConfig.RequiredStatusUnits;
            else maxUnits = -1;
            timer = 0f;
        }

        public void AddUnits(int value)
        {
            if (maxUnits <= - 1) return;
            switch (mode)
            {
                case StatusMode.Active:
                    if (value > 0) timer = 0f;
                    break;
                case StatusMode.Disable:
                    valueUnits += value;
                    timer = 0f;
                    if (valueUnits >= maxUnits)
                    {
                        valueUnits = 0;
                        mode = StatusMode.Active;
                        status.OnEnter();
                    }
                    break;
            }
        }

        public void Update()
        {
            timer += Time.deltaTime;
            switch (mode)
            {
                case StatusMode.Active:
                    status.Tick();
                    if (timer >= status.statusConfig.LifeTime)
                    {
                        status.OnExit();
                        mode = StatusMode.Recharge;
                        timer = 0f;
                    }
                    break;
                case StatusMode.Recharge:
                    if (timer >= status.statusConfig.CooldownRecharge)
                    {
                        mode = StatusMode.Disable;
                        timer = 0f;
                    }
                    break;
                case StatusMode.Disable:
                    if (valueUnits > 0 && timer >= status.statusConfig.CooldownStatusUnits)
                    {
                        timer = 0f;
                        valueUnits--;
                    }
                    break;
            }
        }

        public void Stop()
        {
            if (mode == StatusMode.Active)
            {
                timer = 0f;
                mode = StatusMode.Recharge;
                status.OnExit();
            }
        }

        public enum StatusMode { Disable, Active, Recharge }
    }
}