using UnityEngine;

[CreateAssetMenu(fileName = "CaveGenerationConfig", menuName = "Rift/Cave Generation Config")]
public class CaveGenerationConfig : ScriptableObject
{
    [Header("Граф — Poisson Disk")]
    [Tooltip("Минимальное расстояние между узлами (в вокселях)")]
    public float MinNodeDistance = 20f;

    [Tooltip("Максимум узлов в графе")]
    [Range(6, 20)]
    public int MaxNodes = 14;

    [Tooltip("Отступ от границ мира (в вокселях)")]
    public float WorldMargin = 15f;

    [Header("Граф — Связи")]
    [Tooltip("Доля дополнительных рёбер поверх MST")]
    [Range(0f, 1f)]
    public float ExtraEdgeRatio = 0.5f;

    [Header("Вертикаль")]
    [Tooltip("Базовая высота узлов (0–1 от высоты мира)")]
    [Range(0.2f, 0.8f)]
    public float BaseHeightNormalized = 0.5f;

    [Tooltip("Разброс высоты узлов (в вокселях)")]
    public float HeightVariation = 8f;

    [Header("Геометрия — Радиусы вырезания")]
    [Tooltip("Радиус тоннеля (capsule)")]
    public float TunnelRadius = 4f;

    [Tooltip("Радиус зала")]
    public float HallRadius = 10f;

    [Tooltip("Радиус тупика / POI")]
    public float DeadendRadius = 6f;

    [Tooltip("Радиус зоны спавна")]
    public float SpawnRadius = 8f;

    [Tooltip("Радиус зоны эвакуации")]
    public float ExtractRadius = 8f;

    [Tooltip("Радиус цели миссии")]
    public float ObjectiveRadius = 9f;

    [Header("Форма сечения")]
    [Tooltip("Глубина пола ниже центральной линии (абсолютная, в вокселях). " +
             "Одинаковая для залов и тоннелей → нет ступенек на стыках.")]
    public float FloorDepth = 2.5f;

    [Tooltip("Множитель высоты купола (1 = полусфера, <1 = приплюснутый)")]
    [Range(0.3f, 1.5f)]
    public float CeilingHeightRatio = 0.85f;

    [Header("Шум стен")]
    [Tooltip("Масштаб 3D-шума (меньше = крупнее неровности)")]
    public float NoiseScale = 0.08f;

    [Tooltip("Амплитуда шума (в вокселях)")]
    public float NoiseAmplitude = 2f;

    [Tooltip("Смещение шума для разных уровней")]
    public float NoiseOffset = 0f;

    [Header("SmoothMin")]
    [Tooltip("Фактор сглаживания стыков тоннель↔зал (0 = выкл)")]
    [Range(0f, 6f)]
    public float SmoothBlendFactor = 3f;
}