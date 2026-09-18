using UnityEngine;

public class SlidingState : MovementState
{
    public SlidingState(MovementStateMachine stateMachine) : base(stateMachine) { }
    
    public override void Enter()
    {
        var cfg = SM.Config;
        SM.Motor.Crouch();
        
        float entrySpeed = SM.Motor.Speed;
        SM.Context.SlideEntrySpeed = entrySpeed;
        SM.Context.ExitedFromSlide = false;
        SM.Context.SlideBoostCapSpeed = entrySpeed;
        
        if (entrySpeed >= cfg.EntryBoostThreshold)
        {
            float boostedSpeed = Mathf.Min(entrySpeed * cfg.EntryBoostMultiplier, cfg.MaxBoostedSpeed);
            
            Vector3 direction = SM.Motor.GetMoveDirection();
            if (direction.sqrMagnitude > 0.01f)
            {
                SM.Motor.SetHorizontalVelocity(direction, boostedSpeed);
            }
        }
    }
    
    public override void Exit()
    {
        SM.Motor.ResetHeight();
        
        SM.Context.SlideExitSpeed = SM.Motor.Speed;
        SM.Context.ExitedFromSlide = true;
        SM.Context.SlideExitTime = Time.time;
        SM.Context.PreviousState = MovementType.Sliding;
    }
    
    public override MovementType? CheckTransitions()
    {
        var cfg = SM.Config;
        
        if (!SM.Motor.IsGrounded)
            return MovementType.AirControl;
        
        if (SM.Input.Movement.Jump.Pressed && SM.Motor.CanJump)
        {
            if (!SM.Motor.CanStandUp())
                return null;
            
            return MovementType.Jumping;
        }

        if (SM.Input.Movement.Dash.Pressed)
            return MovementType.Dashing;

        if (SM.Motor.Speed < cfg.MinSlideSpeed)
        {
            if (SM.Input.Movement.Crouch.Held)
                return MovementType.Crouching;
            
            if (!SM.Motor.CanStandUp())
                return MovementType.Crouching;
            
            return SM.Input.Movement.Move.sqrMagnitude > 0.01f
                ? (SM.Input.Movement.Sprint.Held ? MovementType.Sprinting : MovementType.Walking)
                : MovementType.Idle;
        }
        
        if (!SM.Input.Movement.Crouch.Held)
        {
            if (!SM.Motor.CanStandUp())
            {
                if (SM.Motor.Speed < cfg.MinSlideSpeed)
                    return MovementType.Crouching;
                    
                return null;
            }
            
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
        var cfg = SM.Config;
        var input = SM.Input.Movement.Move;
        var wishDirection = new Vector3(input.x, 0, input.y);
        
        SM.Motor.SlideMove(wishDirection, cfg.BaseSlideSpeed, cfg.SlideAcceleration);
    }
}