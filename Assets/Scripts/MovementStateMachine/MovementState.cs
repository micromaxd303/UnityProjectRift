public enum MovementType
{
    Idle,
    Walking,
    Sprinting,
    Crouching,
    Sliding,
    Jumping,
    AirControl,
    Dashing
}

public abstract class MovementState
{
    protected MovementStateMachine SM;

    public MovementState(MovementStateMachine stateMachine)
    {
        SM = stateMachine;
    }
    
    public virtual void Enter() { }
    public virtual void Exit() { }
    public virtual void Update() { }
    public abstract MovementType? CheckTransitions();
}