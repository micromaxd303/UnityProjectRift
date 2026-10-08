using System;
using UnityEngine;

[CreateAssetMenu(fileName = "GraphBuilderConfig", menuName = "Scriptable Objects/GraphBuilderConfig")]
public class GraphBuilderConfig : ScriptableObject
{
    public MitchelSamplerSettings mitchelSamplerSettings;
    public CaveGraphSettings caveGraphSettings;

    [Tooltip("Параметры игрока: высота прыжка, скорость. Если не задан, берутся значения по умолчанию.")]
    public MovementConfig movement;
}

[Serializable]
public class MitchelSamplerSettings
{
    public int desiredNodesCount = 10;
    public int sampleCount = 10;
    public int sampleGrowth = 1;

    [Header("Height")]
    public float heightAmplitude = 20f;
    public float noiseScale = 0.1f;
}

[Serializable]
public class CaveGraphSettings
{
    [Header("Попытки")]
    [Min(1)] public int maxAttempts = 20;

    [Header("Рёбра")]
    [Tooltip("Максимальный уклон обычного туннеля, градусы. Круче — только обрыв или ничего.")]
    [Range(0f, 89f)] public float maxTunnelSlope = 35f;
    [Tooltip("Вес Kruskal: длина × (1 + штраф × уклон/максимальный уклон).")]
    [Min(0f)] public float slopePenalty = 2f;
    [Tooltip("Максимальная высота обрыва, м. Обрывы бывают только петлями, остов из них не строится.")]
    [Min(0f)] public float maxDropHeight = 8f;
    [Tooltip("Доля петель, которые станут лазами.")]
    [Range(0f, 1f)] public float crawlChance = 0.25f;
    [Tooltip("Доля петель с перепадом высот больше прыжка (но не выше maxDropHeight), которые станут обрывами.")]
    [Range(0f, 1f)] public float dropChance = 0.5f;
    [Tooltip("Петля добавляется, только если путь по графу длиннее прямой хотя бы во столько раз.")]
    [Min(1f)] public float minLoopRatio = 1.6f;

    [Header("Число петель как доля от числа узлов")]
    [Range(0f, 1f)] public float loopsFew = 0.05f;
    [Range(0f, 1f)] public float loopsSome = 0.15f;
    [Range(0f, 1f)] public float loopsMany = 0.3f;
    [Range(0f, 1f)] public float loopsAny = 0.1f;

    [Header("Размеры регионов (радиус, м)")]
    public Vector2 smallRadius = new(6f, 8f);
    public Vector2 mediumRadius = new(9f, 13f);
    public Vector2 largeRadius = new(15f, 20f);

    [Header("Оценка длительности (черновые значения)")]
    [Tooltip("Во сколько раз туннель длиннее прямой между узлами.")]
    [Min(1f)] public float tunnelWinding = 1.3f;
    [Tooltip("Доля от скорости ходьбы, с которой игрок реально движется по уровню (бои, осмотр).")]
    [Range(0.1f, 1f)] public float travelEfficiency = 0.5f;
    public float fightSecondsPerObjective = 45f;
    public int crystalsRequired = 12;
    public int carryLimit = 4;
    public float defenseSecondsPerPlatform = 60f;
    public float arenaSeconds = 180f;
    [Tooltip("Выкл: длительность вне целевого диапазона только пишется в лог.")]
    public bool rejectOnDuration = false;
}
