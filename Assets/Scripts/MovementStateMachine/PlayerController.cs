using UnityEngine;
public class PlayerController : MonoBehaviour
{
    private InputManager input;
    private PlayerMotor motor;
    private MovementStateMachine stateMachine;
    
    private void Awake()
    {
        input = GetComponent<InputManager>();
        motor = GetComponent<PlayerMotor>();
        stateMachine = GetComponent<MovementStateMachine>();
    }
    private void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        
        DontDestroyOnLoad(transform.parent.gameObject);
        
        stateMachine.Initialize(input, motor);
        
        stateMachine.RegisterState(MovementType.Idle, new IdleState(stateMachine));
        stateMachine.RegisterState(MovementType.Walking, new WalkState(stateMachine));
        stateMachine.RegisterState(MovementType.Sprinting, new SprintState(stateMachine));
        stateMachine.RegisterState(MovementType.Crouching, new CrouchState(stateMachine));
        stateMachine.RegisterState(MovementType.Sliding, new SlidingState(stateMachine));
        stateMachine.RegisterState(MovementType.Jumping, new JumpingState(stateMachine));
        stateMachine.RegisterState(MovementType.AirControl, new AirControlState(stateMachine));
        stateMachine.RegisterState(MovementType.Dashing, new DashingState(stateMachine));
        
        stateMachine.SetStartingState(MovementType.Idle);
    }

}