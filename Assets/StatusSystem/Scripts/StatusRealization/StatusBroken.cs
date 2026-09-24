using UnityEngine;

[AddComponentMenu("Status System/Status Broken")]
public class StatusBroken : Status
{
    [SerializeField, Tooltip("Уменьшение сопротивлений")]
    private DamageResists addResist;

    [SerializeField]
    private DamageController controller;

    private DamageResists lastResist;

    public override void OnEnter()
    {
        if (controller) 
        { 
            lastResist = new DamageResists(controller.resists);
            controller.resists = controller.resists - addResist;
        }
    }

    public override void Tick()
    {

    }

    public override void OnExit()
    {
        if (controller) controller.resists = new DamageResists(lastResist);
    }
}
