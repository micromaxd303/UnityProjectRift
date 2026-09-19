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
        if (!SM.Motor.IsGrounded)
            return MovementType.AirControl;
        
        if (SM.Input.Movement.Dash.Pressed)
            return MovementType.Dashing;
        
        if (SM.Input.Movement.Jump.Pressed && SM.Motor.CanJump)
            return MovementType.Jumping;
        
        if (SM.Motor.Speed > SM.Config.MinSlideSpeed && SM.Motor.CanStartSlide())
            return MovementType.Sliding;
        
        if (!SM.Input.Movement.Crouch.Held)
        {
            if (!SM.Motor.CanStandUp())
                return null;
            
            if (SM.Input.Movement.Move.sqrMagnitude < 0.01f)
                return MovementType.Idle;
            
            return SM.Input.Movement.Sprint.Held
                ? MovementType.Sprinting
                : MovementType.Walking;
        }
        
        return null;
    }
    
    public override void Update()
    {
        var input = SM.Input.Movement.Move;
        var direction = new Vector3(input.x, 0, input.y).normalized;
        
        SM.Motor.Move(direction, SM.Config.CrouchSpeed, SM.Config.CrouchAcceleration);
    }
}