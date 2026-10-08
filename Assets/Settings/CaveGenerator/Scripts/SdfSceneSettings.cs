using System;
using UnityEngine;

/// <summary>Параметры этапа 2: как граф регионов превращается в SDF-сцену.</summary>
[Serializable]
public class SdfSceneSettings
{
    [Header("Комнаты")]
    [Tooltip("Вертикальный радиус эллипсоида как доля горизонтального.")]
    [Range(0.3f, 1f)] public float roomHeightRatio = 0.6f;
    [Tooltip("Случайный разброс горизонтальных радиусов, чтобы комнаты не были круглыми.")]
    [Range(0f, 0.5f)] public float roomShapeJitter = 0.2f;
    [Tooltip("Где срезается пол: доля вертикального радиуса ниже центра комнаты.")]
    [Range(0f, 0.95f)] public float roomFloorCut = 0.5f;

    [Header("Форма комнат: лепестки")]
    [Tooltip("Сколько дополнительных эллипсоидов-лепестков сливается с основным (мин, макс).")]
    public Vector2Int lobeCount = new(2, 4);
    [Tooltip("Радиус лепестка как доля радиуса комнаты (мин, макс).")]
    public Vector2 lobeRadius = new(0.45f, 0.75f);
    [Tooltip("Расстояние центра лепестка от центра комнаты, доля радиуса (мин, макс).")]
    public Vector2 lobeOffset = new(0.5f, 0.95f);
    [Tooltip("Разброс высоты потолка лепестка (доля от высоты комнаты).")]
    [Range(0f, 0.6f)] public float lobeHeightJitter = 0.3f;
    [Tooltip("Максимальный уступ пола лепестка, м. Держи ниже высоты прыжка, чтобы на уступ можно было забраться.")]
    [Min(0f)] public float lobeFloorStep = 1.5f;
    [Tooltip("Радиус smooth-min между лепестками, м.")]
    [Min(0.01f)] public float roomBlend = 4f;

    [Header("Туннели")]
    [Tooltip("Рост игрока, м. Из него считается высота лаза.")]
    [Min(0.5f)] public float playerHeight = 2f;
    [Min(0.5f)] public float tunnelRadius = 3.5f;
    [Tooltip("Где срезается пол туннеля: доля радиуса ниже оси.")]
    [Range(0f, 0.95f)] public float tunnelFloorCut = 0.6f;
    [Tooltip("Запас по высоте в лазе над ростом игрока в приседе, м.")]
    [Min(0f)] public float crawlClearance = 0.3f;
    [Tooltip("Насколько туннель отклоняется вбок от прямой, доля длины.")]
    [Range(0f, 0.5f)] public float tunnelWiggle = 0.12f;
    [Tooltip("Шаг капсул вдоль кривой, м.")]
    [Min(0.25f)] public float capsuleSpacing = 1.5f;

    [Header("Слияние")]
    [Tooltip("Радиус smooth-min между комнатами и туннелями, м.")]
    [Min(0.01f)] public float blend = 2f;

    [Header("Шум")]
    [Min(0f)] public float noiseAmplitude = 1.2f;
    [Min(0.001f)] public float noiseFrequency = 0.15f;
    [Tooltip("Варп только по горизонтали: стены гнутся, а полы остаются ровными.")]
    [Min(0f)] public float warpAmplitude = 2f;
    [Min(0.001f)] public float warpFrequency = 0.04f;
    [Tooltip("Радиус ровного пятачка в центре площадки (под ДУ) и хаба (под МППУ и раздатчик), доля радиуса комнаты.")]
    [Range(0.1f, 0.9f)] public float padRadius = 0.5f;
    [Tooltip("Максимальный случайный сдвиг пятачка от центра комнаты, доля радиуса комнаты.")]
    [Range(0f, 0.8f)] public float padOffset = 0.3f;
    [Tooltip("Над пятачком шум нарастает от нуля у пола до полной силы на этой высоте, м.")]
    [Min(0.01f)] public float floorNoiseBand = 1.5f;
    [Range(0f, 1f)] public float tunnelNoise = 0.6f;

    [Header("Штампы: ниши инкубаторов (гнёзда)")]
    public bool stampNiches = true;
    [Tooltip("Сколько ниш в стене каждого гнезда (мин, макс).")]
    public Vector2Int nicheCount = new(1, 2);
    [Tooltip("Горизонтальный радиус ниши, м.")]
    [Min(0.5f)] public float nicheRadius = 2.5f;
    [Tooltip("Высота свода ниши над полом, м.")]
    [Min(1f)] public float nicheHeight = 3.5f;
    [Tooltip("Насколько центр ниши уходит в стену за край пола комнаты, м.")]
    [Min(0f)] public float nicheDepth = 1f;

    [Header("Штампы: укрытия на площадках")]
    public bool stampCover = true;
    [Tooltip("Сколько укрытий вокруг места под ДУ (мин, макс).")]
    public Vector2Int coverCount = new(3, 5);
    [Tooltip("Длина укрытия, м (мин, макс).")]
    public Vector2 coverLength = new(2f, 3.5f);
    [Tooltip("Высота укрытия над полом, м. Ниже прыжка — на него можно запрыгнуть.")]
    [Min(0.3f)] public float coverHeight = 1.2f;
    [Min(0.2f)] public float coverThickness = 0.8f;
    [Tooltip("На каком расстоянии от центра пятачка стоят укрытия, доля радиуса пятачка.")]
    [Range(0.3f, 1f)] public float coverRing = 0.75f;

    [Header("Штампы: колонны")]
    public bool stampPillars = true;
    [Tooltip("Колонны ставятся только в комнаты с радиусом не меньше этого, м.")]
    [Min(0f)] public float pillarMinRoomRadius = 12f;
    [Tooltip("Сколько колонн в подходящей комнате (мин, макс).")]
    public Vector2Int pillarCount = new(1, 3);
    [Tooltip("Радиус колонны, м (мин, макс).")]
    public Vector2 pillarRadius = new(1f, 2f);
    [Tooltip("Свободное место вокруг колонны: до туннелей, пятачка, ниш и других колонн, м.")]
    [Min(0f)] public float pillarClearance = 2.5f;

    [Tooltip("Радиус сглаживания штампов со стенами и полом, м.")]
    [Min(0.01f)] public float stampBlend = 1f;
}
