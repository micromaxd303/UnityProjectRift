using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "TransitionMatrixAsset", menuName = "Scriptable Objects/TransitionMatrixAsset")]
public class TransitionMatrixAsset : ScriptableObject
{
    public enum ConditionType
    {
        None,
        SpeedAboveThreshold,
        SpeedBelowThreshold,
        IsGrounded,
        IsNotGrounded
    }
    
    [Serializable]
    public struct TransitionRule
    {
        public MovementType from;
        public MovementType to;
        public TransitionType type;
        public ConditionType condition;
    }

    [SerializeField] private List<TransitionRule> rules = new();

    public TransitionMatrix Build(Func<ConditionType, bool> conditionResolver)
    {
        var matrix = new TransitionMatrix();

        foreach (var rule in rules)
        {
            switch (rule.type)
            {
                case TransitionType.Allowed:
                    matrix.Allow(rule.from, rule.to);
                    break;
                case TransitionType.Blocked:
                    matrix.Block(rule.from, rule.to);
                    break;
                case TransitionType.Conditional:
                    var condition = rule.condition;
                    matrix.Conditional(rule.from, rule.to, () => conditionResolver(condition));
                    break;
            }
        }
        
        return matrix;
    }

    [ContextMenu("Generate All Combinations")]
    private void GenerateAllCombinations()
    {
        
#if UNITY_EDITOR
        if (rules.Count > 0)
        {
            if (!UnityEditor.EditorUtility.DisplayDialog(
                    "Generate Transitions",
                    $"This will replace {rules.Count} existing rules. Continue?",
                    "Replace", "Cancel"))
            {
                return;
            }
        }
#endif
        
        rules.Clear();
        
        var types = (MovementType[])Enum.GetValues(typeof(MovementType));

        foreach (var from in types)
        {
            foreach (var to in types)
            {
                if (from == to) continue;
                
                var (transitionType, conditionType) = GetDefaultTransition(from, to);
                
                rules.Add(new TransitionRule
                {
                    from = from,
                    to = to,
                    type = transitionType,
                    condition = conditionType
                });
            }
        }
    }
    
    private (TransitionType, ConditionType) GetDefaultTransition(MovementType from, MovementType to)
    {
        // Из Idle
        if (from == MovementType.Idle && to == MovementType.Walking)
            return (TransitionType.Allowed, ConditionType.None);
        if (from == MovementType.Idle && to == MovementType.Sprinting)
            return (TransitionType.Allowed, ConditionType.None);
        if (from == MovementType.Idle && to == MovementType.Crouching)
            return (TransitionType.Allowed, ConditionType.None);
        if (from == MovementType.Idle && to == MovementType.Jumping)
            return (TransitionType.Allowed, ConditionType.None);
        if (from == MovementType.Idle && to == MovementType.AirControl)
            return (TransitionType.Allowed, ConditionType.None);
        
        // Из Walking
        if (from == MovementType.Walking && to == MovementType.Idle)
            return (TransitionType.Allowed, ConditionType.None);
        if (from == MovementType.Walking && to == MovementType.Sprinting)
            return (TransitionType.Allowed, ConditionType.None);
        if (from == MovementType.Walking && to == MovementType.Crouching)
            return (TransitionType.Allowed, ConditionType.None);
        if (from == MovementType.Walking && to == MovementType.Jumping)
            return (TransitionType.Allowed, ConditionType.None);
        if (from == MovementType.Walking && to == MovementType.AirControl)
            return (TransitionType.Allowed, ConditionType.None);
        if (from == MovementType.Walking && to == MovementType.Dashing)
            return (TransitionType.Allowed, ConditionType.None);
        
        // Из Sprinting
        if (from == MovementType.Sprinting && to == MovementType.Idle)
            return (TransitionType.Allowed, ConditionType.None);
        if (from == MovementType.Sprinting && to == MovementType.Walking)
            return (TransitionType.Allowed, ConditionType.None);
        if (from == MovementType.Sprinting && to == MovementType.Crouching)
            return (TransitionType.Allowed, ConditionType.None);
        if (from == MovementType.Sprinting && to == MovementType.Jumping)
            return (TransitionType.Allowed, ConditionType.None);
        if (from == MovementType.Sprinting && to == MovementType.AirControl)
            return (TransitionType.Allowed, ConditionType.None);
        if (from == MovementType.Sprinting && to == MovementType.Dashing)
            return (TransitionType.Allowed, ConditionType.None);
        
        // Из Crouching
        if (from == MovementType.Crouching && to == MovementType.Idle)
            return (TransitionType.Allowed, ConditionType.None);
        if (from == MovementType.Crouching && to == MovementType.Walking)
            return (TransitionType.Allowed, ConditionType.None);
        if (from == MovementType.Crouching && to == MovementType.Sprinting)
            return (TransitionType.Allowed, ConditionType.None);
        if (from == MovementType.Crouching && to == MovementType.Sliding)
            return (TransitionType.Conditional, ConditionType.SpeedAboveThreshold);
        if (from == MovementType.Crouching && to == MovementType.AirControl)
            return (TransitionType.Allowed, ConditionType.None);
        if (from == MovementType.Crouching && to == MovementType.Dashing)
            return (TransitionType.Allowed, ConditionType.None);
        
        // Из Sliding
        if (from == MovementType.Sliding && to == MovementType.Idle)
            return (TransitionType.Allowed, ConditionType.None);
        if (from == MovementType.Sliding && to == MovementType.Walking)
            return (TransitionType.Allowed, ConditionType.None);
        if (from == MovementType.Sliding && to == MovementType.Sprinting)
            return (TransitionType.Allowed, ConditionType.None);
        if (from == MovementType.Sliding && to == MovementType.Crouching)
            return (TransitionType.Conditional, ConditionType.SpeedBelowThreshold);
        if (from == MovementType.Sliding && to == MovementType.Jumping)
            return (TransitionType.Allowed, ConditionType.None);
        if (from == MovementType.Sliding && to == MovementType.AirControl)
            return (TransitionType.Allowed, ConditionType.None);
        if (from == MovementType.Sliding && to == MovementType.Dashing)
            return (TransitionType.Allowed, ConditionType.None);
        
        // Из Jumping
        if (from == MovementType.Jumping && to == MovementType.AirControl)
            return (TransitionType.Allowed, ConditionType.None);
        
        // Из AirControl
        if (from == MovementType.AirControl && to == MovementType.Idle)
            return (TransitionType.Allowed, ConditionType.None);
        if (from == MovementType.AirControl && to == MovementType.Walking)
            return (TransitionType.Allowed, ConditionType.None);
        if (from == MovementType.AirControl && to == MovementType.Sprinting)
            return (TransitionType.Allowed, ConditionType.None);
        if (from == MovementType.AirControl && to == MovementType.Crouching)
            return (TransitionType.Allowed, ConditionType.None);
        if (from == MovementType.AirControl && to == MovementType.Dashing)
            return (TransitionType.Allowed, ConditionType.None);
        
        // Из Dashing
        if (from == MovementType.Dashing && to == MovementType.Idle)
            return (TransitionType.Allowed, ConditionType.None);
        if (from == MovementType.Dashing && to == MovementType.Walking)
            return (TransitionType.Allowed, ConditionType.None);
        if (from == MovementType.Dashing && to == MovementType.Sprinting)
            return (TransitionType.Allowed, ConditionType.None);
        if (from == MovementType.Dashing && to == MovementType.Crouching)
            return (TransitionType.Allowed, ConditionType.None);
        if (from == MovementType.Dashing && to == MovementType.AirControl)
            return (TransitionType.Allowed, ConditionType.None);
        
        // Всё остальное — заблокировано
        return (TransitionType.Blocked, ConditionType.None);
    }
}