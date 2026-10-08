using System;
using UnityEngine;

/// <summary>Параметры этапа 4: анализ и проверка уровня перед мешированием.</summary>
[Serializable]
public class LevelAnalysisSettings
{
    [Tooltip("Выкл: поле сразу идёт в MC без проверки.")]
    public bool enabled = true;

    [Header("Попытки")]
    [Tooltip("Сколько раз чинить туннели, прежде чем строить новый граф.")]
    [Min(0)] public int maxRepairAttempts = 3;
    [Tooltip("Сколько раз строить новый граф (новый подсид), если ремонт не помог.")]
    [Min(1)] public int maxTopologyAttempts = 5;
    [Tooltip("Во сколько раз расширяется туннель при ремонте. Пол остаётся на месте, растут ширина и потолок.")]
    [Range(1f, 2f)] public float repairInflate = 1.3f;

    [Header("Правила игрока")]
    [Tooltip("С какой высоты игрок может спрыгнуть, м.")]
    [Min(0f)] public float maxFallHeight = 10f;
    [Tooltip("Запас к MaxSlopeAngle из MovementConfig на ступенчатость вокселей, градусы.")]
    [Range(0f, 30f)] public float slopeTolerance = 10f;
    [Tooltip("Проверять, что из каждой комнаты можно вернуться к старту.")]
    public bool requireReturnToStart = true;
}
