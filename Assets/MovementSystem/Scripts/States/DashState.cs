using UnityEngine;

public class DashingState : MovementState
{
    public DashingState(MovementStateMachine stateMachine) : base(stateMachine) { }
    
    public override void Enter()
    {
        SM.Motor.StartDash(SM.Input.Movement.Move);
    }
    
    public override void Exit()
    {
        if (SM.Motor.IsDashing)
        {
            SM.Motor.EndDash();
        }
    }
    
    public override MovementType? CheckTransitions()
    {
        if (SM.Motor.IsDashing)
            return null;
        
        if (!SM.Motor.IsGrounded)
            return MovementType.AirControl;
        
        if (SM.Input.Movement.Crouch.Pressed)
            return MovementType.Crouching;
        
        if (SM.Input.Movement.Move.magnitude > 0.1f)
        {
            return SM.Input.Movement.Sprint.Held
                ? MovementType.Sprinting
                : MovementType.Walking;
        }
        
        return MovementType.Idle;
    }

    public override void Update()
    {
        SM.Motor.UpdateDash();
    }
}