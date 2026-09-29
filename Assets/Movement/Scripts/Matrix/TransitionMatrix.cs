using System;
using System.Collections.Generic;
using UnityEngine;

public enum TransitionType
{
    Blocked,
    Allowed,
    Conditional
}

public class TransitionMatrix
{
    private readonly Dictionary<(MovementType from, MovementType to), TransitionType> transitions = new();
    private readonly Dictionary<(MovementType from, MovementType to), Func<bool>> conditions = new();

    public void Allow(MovementType from, MovementType to)
    {
        transitions[(from, to)] = TransitionType.Allowed;
        conditions.Remove((from, to));
    }

    public void Block(MovementType from, MovementType to)
    {
        transitions[(from, to)] = TransitionType.Blocked;
        conditions.Remove((from, to));
    }

    public void Conditional(MovementType from, MovementType to, Func<bool> condition)
    {
        transitions[(from, to)] = TransitionType.Conditional;
        conditions[(from, to)] = condition;
    }

    public bool CanTransition(MovementType from, MovementType to)
    {
        var key = (from, to);
        if (!transitions.TryGetValue(key, out var type))
        {
#if UNITY_EDITOR
            Debug.LogWarning($"Transition {from} → {to} not registered in matrix");
#endif
            return false;
        }

        return type switch
        {
            TransitionType.Allowed => true,
            TransitionType.Blocked => false,
            TransitionType.Conditional => conditions.TryGetValue(key, out var condition) && condition(),
            _ => false
        };
    }
}