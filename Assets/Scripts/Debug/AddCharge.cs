using UnityEngine;

public class AddCharge : MonoBehaviour
{
    public PlayerMotor motor;

    void OnTriggerEnter(Collider other)
    {
        Debug.Log("AddCharge collided");
        
        motor.SetMaxDashCharges(1);
    }
    
    
}
