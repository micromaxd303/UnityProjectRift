using UnityEngine;

public class TeleportScript : MonoBehaviour
{
    public Transform point;
    public PlayerMotor motor;
    void OnTriggerEnter(Collider other)
    {
        motor.Teleport(point.position);
    }
    
    
    
}