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
        
        if (SM.Input.JumpPressed)
            return MovementType.Jumping;
        
        if (SM.Input.CrouchPressed)
            return MovementType.Crouching;
        
        if (SM.Input.MoveInput.sqrMagnitude > 0.01f)
        {
            return SM.Input.SprintHeld 
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