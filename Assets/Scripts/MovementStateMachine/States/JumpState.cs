using UnityEngine;

public class JumpingState : MovementState
{
    private float timer;
    private bool wasSlideJump;
    
    public JumpingState(MovementStateMachine stateMachine) : base(stateMachine) { }
    
    public override void Enter()
    {
        var cfg = SM.Config;
        timer = cfg.MinJumpTime;
        wasSlideJump = false;
        
        float jumpForce = cfg.BaseJumpForce;
        
        if (SM.Context.CanSlideJump() && SM.Context.SlideExitSpeed >= cfg.SlideJumpMinSpeed)
        {
            wasSlideJump = true;
            jumpForce += cfg.SlideJumpForceBonus;
            
            Vector3 direction = SM.Motor.GetMoveDirection();
            if (direction.sqrMagnitude > 0.01f)
            {
                float currentSpeed = SM.Motor.Speed;
                float boostedSpeed = Mathf.Min(currentSpeed * cfg.SlideJumpSpeedBoost, cfg.MaxSlideJumpSpeed);
                
                if (currentSpeed < cfg.MaxSlideJumpSpeed)
                {
                    SM.Motor.SetHorizontalVelocity(direction, boostedSpeed);
                }
            }
        }
        
        SM.Motor.Jump(jumpForce);
        SM.Context.ExitedFromSlide = false;
    }
    
    public override void Exit()
    {
        SM.Context.PreviousState = MovementType.Jumping;
    }
    
    public override MovementType? CheckTransitions()
    {
        if (timer > 0)
            return null;
        
        if (SM.Motor.IsGrounded)
        {
            if (!SM.Motor.CanStandUp())
                return MovementType.Crouching;
            
            if (SM.Input.MoveInput.sqrMagnitude > 0.01f)
                return SM.Input.SprintHeld ? MovementType.Sprinting : MovementType.Walking;
            return MovementType.Idle;
        }
        
        return MovementType.AirControl;
    }
    
    public override void Update()
    {
        timer -= Time.deltaTime;
        
        var input = SM.Input.MoveInput;
        if (input.sqrMagnitude < 0.01f) return;
        
        var direction = new Vector3(input.x, 0, input.y).normalized;
        
        float airControl = wasSlideJump ? SM.Config.SlideJumpAirControlMultiplier : 1f;
        SM.Motor.AirMove(direction, SM.Motor.Speed, airControl);
    }
}