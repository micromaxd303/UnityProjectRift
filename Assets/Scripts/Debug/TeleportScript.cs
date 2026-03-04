using UnityEngine;

public class TeleportScript : MonoBehaviour
{
    public Transform point;
    public PlayerMotor motor;
    
    public Vector2 direction = new Vector2(0,0);


    void OnTriggerEnter(Collider other)
    {
        if(direction != Vector2.zero)
            motor.Teleport(point.position);
        else 
            motor.Teleport(point.position, direction);
    }
    
    
    
}