using UnityEngine;
public class MovementContext
{
    public float SlideEntrySpeed { get; set; }
    public float SlideExitSpeed { get; set; }
    public bool ExitedFromSlide { get; set; }
    public MovementType PreviousState { get; set; }
    public float SlideExitTime { get; set; }
    public float SlideBoostCapSpeed { get; set; }
    
    public const float SlideJumpWindow = 0.3f;
    
    public bool CanSlideJump()
    {
        return ExitedFromSlide && 
               (Time.time - SlideExitTime) < SlideJumpWindow;
    }
    
    public void Reset()
    {
        SlideEntrySpeed = 0f;
        SlideExitSpeed = 0f;
        ExitedFromSlide = false;
        SlideExitTime = -999f;
        SlideBoostCapSpeed = 0f;
        PreviousState = MovementType.Idle;
    }
}