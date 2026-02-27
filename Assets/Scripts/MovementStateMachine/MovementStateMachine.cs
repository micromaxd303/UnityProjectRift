using System;
using System.Collections.Generic;
using UnityEngine;

public class MovementStateMachine : MonoBehaviour
{
    [SerializeField] private TransitionMatrixAsset transitionMatrixAsset;
    [SerializeField] private MovementConfig config;
    
    public InputManager Input { get; private set; }
    public PlayerMotor Motor { get; private set; }
    public MovementConfig Config => config;
    
    public MovementType CurrentStateType { get; private set; }
    public MovementState CurrentState { get; private set; }
    
    public MovementContext Context { get; private set; } = new();

    private readonly Dictionary<MovementType, MovementState> states = new();
    private TransitionMatrix transitionMatrix = new();
    
    public event Action<MovementType, MovementType> OnStateChanged;

    public void Initialize(InputManager input, PlayerMotor motor)
    {
        Input = input;
        Motor = motor;
        
        if (transitionMatrixAsset != null)
        {
            transitionMatrix = transitionMatrixAsset.Build(ResolveCondition);
        }
    }

    private bool ResolveCondition(TransitionMatrixAsset.ConditionType condition)
    {
        return condition switch
        {
            TransitionMatrixAsset.ConditionType.SpeedAboveThreshold =>
                Motor.Speed > config.MinSlideSpeed,

            TransitionMatrixAsset.ConditionType.SpeedBelowThreshold =>
                Motor.Speed < config.MinSlideSpeed,

            TransitionMatrixAsset.ConditionType.IsGrounded =>
                Motor.IsGrounded,

            TransitionMatrixAsset.ConditionType.IsNotGrounded =>
                !Motor.IsGrounded,

            _ => true
        };
    }

    public void RegisterState(MovementType type, MovementState state)
    {
        states[type] = state;
    }

    public TransitionMatrix GetTransitionMatrix() => transitionMatrix;

    public void SetStartingState(MovementType type)
    {
        if (states.TryGetValue(type, out var state))
        {
            CurrentStateType = type;
            CurrentState = state;
            CurrentState.Enter();
        }
    }

    public bool TryChangeState(MovementType newType)
    {
        if (!transitionMatrix.CanTransition(CurrentStateType, newType))
            return false;
        
        ChangeState(newType);
        return true;
    }

    public void ForceChangeState(MovementType newType)
    {
#if UNITY_EDITOR
        Debug.LogWarning($"Forced state transition to: {newType}");
#endif
        ChangeState(newType);
    }

    private void ChangeState(MovementType newType)
    {
        if (!states.TryGetValue(newType, out var newState))
        {
#if UNITY_EDITOR
            Debug.LogWarning($"State {newType} not registered");
#endif
            return;
        }

        if (newType == CurrentStateType)
        {
#if UNITY_EDITOR
            Debug.Log($"Attempted transition to current state: {newType}");
#endif
            return;
        }
        
        var previousState = CurrentStateType;
        
        CurrentState?.Exit();
        CurrentStateType = newType;
        CurrentState = newState;
        CurrentState.Enter();
        
        OnStateChanged?.Invoke(previousState, newType);
    }
    
    private void Update()
    {
        if (CurrentState == null)
            return;
        
        // Обрабатываем телепорт ДО любой логики движения
        if (Motor.ProcessTeleport())
            return; // Пропускаем этот кадр
        
        var nextState = CurrentState.CheckTransitions();
        
        if (nextState.HasValue)
        {
            TryChangeState(nextState.Value);
            // Не вызываем Update — новое состояние начнёт работать со следующего кадра
        }
        else
        {
            CurrentState.Update();
        }
        
        Motor.ApplyGravity();
        Motor.ApplyMovement();
    }
}