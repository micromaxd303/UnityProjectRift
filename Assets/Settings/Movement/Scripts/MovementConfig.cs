using UnityEngine;

[CreateAssetMenu(fileName = "MovementConfig", menuName = "Scriptable Objects/MovementConfig")]
public class MovementConfig : ScriptableObject
{
    [Header("General Physics")]
    public float Gravity = 20f;
    public float GroundStickForce = 5f;
    
    [Header("Ground Detection")]
    public float GroundCheckDistance = 0.3f;
    public float MaxSlopeAngle = 45f;
    public float SnapToGroundDistance = 0.5f;
    public float GroundedGraceTime = 0.1f;
    public LayerMask groundMask = ~0;
    
    [Header("Coyote Time")]
    public float CoyoteTimeDuration = 0.15f;
    
    [Header("Walking")]
    public float WalkSpeed = 10f;
    public float WalkAcceleration = 50f;
    
    [Header("Sprinting")]
    public float SprintSpeed = 16f;
    public float SprintAcceleration = 80f;
    
    [Header("Crouching")]
    public float CrouchSpeed = 5f;
    public float CrouchAcceleration = 70f;
    public float CrouchHeightMultiplier = 0.5f;
    public float CrouchTransitionSpeed = 10f;
    
    [Header("Idle")]
    public float IdleDeceleration = 100f;
    
    [Header("Air Control")]
    public float AirSpeed = 15f;
    public float AirAcceleration = 70f;
    
    [Header("Jump")]
    public float BaseJumpForce = 10f;
    public float MinJumpTime = 0.1f;
    
    [Header("Slide Jump")]
    public float SlideJumpForceBonus = 2f;
    public float SlideJumpSpeedBoost = 1.2f;
    public float SlideJumpMinSpeed = 6f;
    public float MaxSlideJumpSpeed = 60f;
    [Tooltip("Air control multiplier after slide-jump (0-1)")]
    [Range(0f, 1f)]
    public float SlideJumpAirControlMultiplier = 0.5f;
    
    [Header("Sliding")]
    public float BaseSlideSpeed = 15f;
    public float SlideAcceleration = 50f;
    public float MinSlideSpeed = 3f;
    
    [Header("Slide Entry Boost")]
    public float EntryBoostMultiplier = 1.15f;
    public float EntryBoostThreshold = 8f;
    public float MaxBoostedSpeed = 25f;
    
    [Header("Dash")]
    public int MaxDashCharges = 2;
    public float DashChargeRegenTime = 2f;
    public float DashDistance = 8f;
    public float DashDuration = 0.1f;
    public float DashCooldownBetweenUses = 0.1f;
    public float DashMomentumPreserve = 0.3f;
}