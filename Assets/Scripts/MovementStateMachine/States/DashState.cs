using UnityEngine;

public class DashingState : MovementState
{
    public DashingState(MovementStateMachine stateMachine) : base(stateMachine) { }
    
    public override void Enter()
    {
        SM.Motor.StartDash(SM.Input.MoveInput);
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
        
        if (SM.Input.CrouchHeld)
            return MovementType.Crouching;
        
        if (SM.Input.MoveInput.magnitude > 0.1f)
        {
            return SM.Input.SprintHeld
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