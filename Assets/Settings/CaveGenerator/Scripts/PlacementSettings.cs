using System;
using UnityEngine;

/// <summary>Параметры этапа 7: разломы и точки врагов.</summary>
[Serializable]
public class PlacementSettings
{
    [Tooltip("Выкл: LevelContext без разломов и точек врагов.")]
    public bool enabled = true;

    [Header("Разломы")]
    [Tooltip("Высота разрыва над полом, м (мин, макс).")]
    public Vector2 riftHeight = new(1.5f, 2.5f);
    [Tooltip("Свободное место вокруг разрыва до стен и потолка, м.")]
    [Min(0.5f)] public float riftClearance = 1.5f;
    [Tooltip("Минимальное расстояние между разломами, м.")]
    [Min(1f)] public float riftSpacing = 7f;
    [Tooltip("Площадь пола на один разлом сверх минимума из требований миссии, м².")]
    [Min(10f)] public float floorAreaPerRift = 120f;
    [Min(1)] public int maxRiftsPerRegion = 8;

    [Header("Лучи разлома")]
    [Tooltip("На каком расстоянии от разрыва по горизонтали лучи бьют в пол, м (мин, макс).")]
    public Vector2 beamRange = new(2f, 8f);
    [Tooltip("Сколько точек попадания у одного разлома.")]
    [Min(1)] public int groundPointsPerRift = 3;
    [Min(0.5f)] public float groundPointSpacing = 2.5f;

    [Header("Место под врага")]
    [Tooltip("Высота самого крупного врага (Молотильник), м: столько воздуха нужно над точкой выхода.")]
    [Min(0.5f)] public float enemyHeight = 3f;
    [Tooltip("Радиус самого крупного врага, м: столько до стены на уровне корпуса.")]
    [Min(0.2f)] public float enemyRadius = 1.2f;
    [Tooltip("Дополнительный запас до стены сверх радиуса врага, м: чтобы враг не вылезал впритык к стене.")]
    [Min(0f)] public float wallMargin = 1f;

    [Header("Где не ставить")]
    [Tooltip("Расстояние по полу от входов туннелей, м: враг не должен вылезти в проходе.")]
    [Min(0f)] public float tunnelKeepAway = 4f;
    [Tooltip("Расстояние от ниш, пятачков, укрытий и колонн, м.")]
    [Min(0f)] public float anchorKeepAway = 3.5f;
    [Tooltip("Расстояние от спавна игрока, м.")]
    [Min(0f)] public float playerSpawnKeepAway = 15f;

    [Header("Начальные враги")]
    [Tooltip("Площадь пола на одну точку начального врага, м².")]
    [Min(5f)] public float floorAreaPerSpawn = 40f;
    [Min(0)] public int maxSpawnPointsPerRegion = 12;
    [Min(0.5f)] public float spawnSpacing = 3f;
    [Tooltip("Расстояние от точек начальных врагов до разломов и мест попадания лучей, м.")]
    [Min(0f)] public float spawnRiftKeepAway = 5f;
    [Tooltip("В стартовой комнате начальных врагов нет.")]
    public bool noSpawnsInStart = true;

    [Header("Месторождения: кристаллы в стенах")]
    [Tooltip("Сколько мест под кристаллы в стенах каждого месторождения (мин, макс).")]
    public Vector2Int veinCount = new(3, 6);
    [Tooltip("Высота места над полом, м (мин, макс): чтобы игрок дотянулся взрывчаткой.")]
    public Vector2 veinHeight = new(0.5f, 2.5f);
    [Min(0.5f)] public float veinSpacing = 2.5f;
    [Tooltip("Насколько круче горизонтали должна быть стена под кристалл, градусы. Стены комнат у пола наклонные, как чаша.")]
    [Range(10f, 90f)] public float minVeinSteepness = 35f;

    [Header("Кристаллы на потолке")]
    public bool ceilingCrystals = true;
    [Tooltip("Площадь потолка на одну точку, м².")]
    [Min(4f)] public float ceilingAreaPerCrystal = 30f;
    [Tooltip("В туннелях точек реже во столько раз.")]
    [Min(1f)] public float tunnelCrystalRarity = 2f;
    [Min(0.5f)] public float crystalSpacing = 4f;
    [Tooltip("Насколько потолок может отклоняться от горизонтали, градусы. Кристалл встаёт по нормали.")]
    [Range(0f, 80f)] public float maxCeilingTilt = 45f;
    [Tooltip("Радиус основания самого маленького и самого большого префаба, м.")]
    public Vector2 crystalRadius = new(0.4f, 2.5f);
    [Tooltip("Насколько потолок может отходить от плоскости под основанием, м: больше — префаб висит краем.")]
    [Min(0.05f)] public float ceilingFlatness = 0.3f;
    [Tooltip("Сколько места оставить под кристаллом для игрока, м.")]
    [Min(0f)] public float headroomUnderCrystal = 2.5f;
}
