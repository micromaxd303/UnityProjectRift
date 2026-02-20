using UnityEngine;

public class CrouchState : MovementState
{
    public CrouchState(MovementStateMachine stateMachine) : base(stateMachine) { }
    
    public override void Enter()
    {
        SM.Motor.Crouch();
    }
    
    public override void Exit()
    {
        SM.Motor.ResetHeight();
    }
    
    public override MovementType? CheckTransitions()
    {
        if (!SM.Motor.IsGrounded && SM.Motor.TimeSinceGrounded > 0.4f)
            return MovementType.AirControl;
        
        if (SM.Input.DashPressed)
            return MovementType.Dashing;
        
        if (SM.Input.JumpPressed && SM.Motor.CanJump)
            return MovementType.Jumping;
        
        if (SM.Motor.Speed > SM.Config.MinSlideSpeed && SM.Motor.CanStartSlide())
            return MovementType.Sliding;
        
        if (!SM.Input.CrouchHeld)
        {
            if (!SM.Motor.CanStandUp())
                return null;
            
            if (SM.Input.MoveInput.sqrMagnitude < 0.01f)
                return MovementType.Idle;
            
            return SM.Input.SprintHeld
                ? MovementType.Sprinting
                : MovementType.Walking;
        }
        
        return null;
    }
    
    public override void Update()
    {
        var input = SM.Input.MoveInput;
        var direction = new Vector3(input.x, 0, input.y).normalized;
        
        SM.Motor.Move(direction, SM.Config.CrouchSpeed, SM.Config.CrouchAcceleration);
    }
}