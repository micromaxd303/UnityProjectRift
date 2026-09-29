using UnityEngine;

public class WalkState : MovementState
{
    public WalkState(MovementStateMachine stateMachine) : base(stateMachine) { }
    
    public override MovementType? CheckTransitions()
    {
        if (!SM.Motor.IsGrounded)
            return MovementType.AirControl;
        
        if (SM.Input.Movement.Jump.Pressed)
            return MovementType.Jumping;
        
        if (SM.Input.Movement.Dash.Pressed)
            return MovementType.Dashing;
        
        if (SM.Input.Movement.Crouch.Pressed)
            return MovementType.Crouching;
        
        if (SM.Input.Movement.Move.sqrMagnitude < 0.01f)
            return MovementType.Idle;
        
        if (SM.Input.Movement.Sprint.Held)
            return MovementType.Sprinting;
        
        return null;
    }
    
    public override void Update()
    {
        var input = SM.Input.Movement.Move;
        if (input.sqrMagnitude < 0.01f) return;
        var direction = new Vector3(input.x, 0, input.y).normalized;
        
        SM.Motor.Move(direction, SM.Config.WalkSpeed, SM.Config.WalkAcceleration);
    }
}