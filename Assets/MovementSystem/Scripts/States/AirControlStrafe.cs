using UnityEngine;

public class AirControlState : MovementState
{
    public AirControlState(MovementStateMachine stateMachine) : base(stateMachine) { }
    
    public override MovementType? CheckTransitions()
    {
        if (SM.Input.Movement.Dash.Pressed)
            return MovementType.Dashing;
        
        if (SM.Motor.IsGrounded)
        {
            if (SM.Input.Movement.Crouch.Pressed || !SM.Motor.CanStandUp())
                return MovementType.Crouching;
            
            if (SM.Input.Movement.Move.sqrMagnitude > 0.01f)
            {
                return SM.Input.Movement.Sprint.Held
                    ? MovementType.Sprinting
                    : MovementType.Walking;
            }
            
            return MovementType.Idle;
        }
        
        return null;
    }
    
    public override void Update()
    {
        var input = SM.Input.Movement.Move;
        if (input.sqrMagnitude < 0.01f) return;
        var wishDirection = new Vector3(input.x, 0, input.y).normalized;
        
        SM.Motor.AirMove(wishDirection, SM.Config.AirSpeed, SM.Config.AirAcceleration);
    }
}