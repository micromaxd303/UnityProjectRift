using JetBrains.Annotations;
using UnityEngine;

[AddComponentMenu("Status System/Status Broken")]
public class StatusBroken : Status
{
    [Tooltip("Добавки сопротивления на каждый тип патронов в порядке DamageType")]
    public float[] damageAdders;

    [SerializeField]
    private DamageController controller;

    private float[] lastResist;

    public override void OnEnter()
    {
        if (controller) 
        { 
            lastResist = new float[controller.ResistMultiplers.Length];
            for (int i = 0; i < lastResist.Length; i++)
            {
                lastResist[i] = controller.ResistMultiplers[i];
            }
            int min = Mathf.Min(controller.ResistMultiplers.Length, damageAdders.Length);
            for (int i = 0; i < min; i++)
            {
                controller.ResistMultiplers[i] += damageAdders[i];
            }
        }
    }

    public override void Tick()
    {

    }

    public override void OnExit()
    {
        if (controller) controller.ResistMultiplers = lastResist;
    }
}
