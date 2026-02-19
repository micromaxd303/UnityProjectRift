using UnityEngine;

public class IdleState : MovementState
{
    public IdleState(MovementStateMachine stateMachine) : base(stateMachine) { }
    
    public override void Enter()
    {
        // Можно добавить анимацию idle
    }
    
    public override MovementType? CheckTransitions()
    {
        // Упал с платформы
        if (!SM.Motor.IsGrounded)
            return MovementType.AirControl;
        
        // Прыжок
        if (SM.Input.JumpPressed)
            return MovementType.Jumping;
        
        // Присед
        if (SM.Input.CrouchPressed)
            return MovementType.Crouching;
        
        // Движение
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