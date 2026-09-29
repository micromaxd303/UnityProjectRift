using UnityEngine;

public class IdleState : MovementState
{
    public IdleState(MovementStateMachine stateMachine) : base(stateMachine) { }
    
    public override void Enter()
    {
    }
    
    public override MovementType? CheckTransitions()
    {
        if (!SM.Motor.IsGrounded)
            return MovementType.AirControl;
        
        if (SM.Input.Movement.Jump.Pressed)
            return MovementType.Jumping;
        
        if (SM.Input.Movement.Crouch.Pressed)
            return MovementType.Crouching;
        
        if (SM.Input.Movement.Move.sqrMagnitude > 0.01f)
        {
            return SM.Input.Movement.Sprint.Held 
                ? MovementType.Sprinting 
                : MovementType.Walking;
        }
        
        return null;
    }
    
    public override void Update()
    {
        SM.Motor.Move(Vector3.zero, 0, SM.Config.IdleDeceleration);
    }
}