using UnityEngine;

public class SprintState : MovementState
{
    public SprintState(MovementStateMachine stateMachine) : base(stateMachine) { }
    
    public override MovementType? CheckTransitions()
    {
        if (!SM.Motor.IsGrounded)
            return MovementType.AirControl;
        
        if (SM.Input.JumpPressed)
            return MovementType.Jumping;
        
        if (SM.Input.DashPressed)
            return MovementType.Dashing;
        
        if (SM.Input.CrouchPressed)
            return MovementType.Crouching;
        
        if (SM.Input.MoveInput.sqrMagnitude < 0.01f)
            return MovementType.Idle;
        
        if (!SM.Input.SprintHeld)
            return MovementType.Walking;
        
        return null;
    }
    
    public override void Update()
    {
        var input = SM.Input.MoveInput;
        if (input.sqrMagnitude < 0.01f) return;
        var direction = new Vector3(input.x, 0, input.y).normalized;
        
        SM.Motor.Move(direction, SM.Config.SprintSpeed, SM.Config.SprintAcceleration);
    }
}