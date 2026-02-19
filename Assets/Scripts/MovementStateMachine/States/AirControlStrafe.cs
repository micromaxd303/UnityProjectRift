using UnityEngine;

public class AirControlState : MovementState
{
    public AirControlState(MovementStateMachine stateMachine) : base(stateMachine) { }
    
    public override MovementType? CheckTransitions()
    {
        if (SM.Input.DashPressed)
            return MovementType.Dashing;
        
        if (SM.Motor.IsGrounded)
        {
            if (SM.Input.CrouchHeld)
                return MovementType.Crouching;
            
            if (SM.Input.MoveInput.sqrMagnitude > 0.01f)
            {
                return SM.Input.SprintHeld
                    ? MovementType.Sprinting
                    : MovementType.Walking;
            }
            
            return MovementType.Idle;
        }
        
        return null;
    }
    
    public override void Update()
    {
        var input = SM.Input.MoveInput;
        if (input.sqrMagnitude < 0.01f) return;
        var wishDirection = new Vector3(input.x, 0, input.y).normalized;
        
        SM.Motor.AirMove(wishDirection, SM.Config.AirSpeed, SM.Config.AirAcceleration);
    }
}