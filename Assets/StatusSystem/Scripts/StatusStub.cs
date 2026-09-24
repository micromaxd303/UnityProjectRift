using UnityEngine;

[AddComponentMenu("Status System/Status Stub")]
public class StatusStub : Status
{
    public override void OnEnter()
    {
        Debug.Log("статус активирован");
    }

    public override void Tick()
    {
        Debug.Log("статус выполянется");
    }

    public override void OnExit()
    {
        Debug.Log("статус деактивирован");
    }
}
