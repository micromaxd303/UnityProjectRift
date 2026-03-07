using UnityEngine;

public class ChargeController : MonoBehaviour
{
    public PlayerMotor motor;

    public bool addOrRemove = false;
    
    public bool regenCharge = false;

    void OnTriggerEnter(Collider other)
    {
        if (addOrRemove)
        {
            motor.SetMaxDashCharges(motor.CurrentDashCharges + 1);
        }
        else if (regenCharge)
        {
            motor.AddDashCharges(1);
        }
        else
        {
            motor.SetMaxDashCharges(0);
        }
    }
}
