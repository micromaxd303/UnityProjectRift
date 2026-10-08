using System.Collections.Generic;
using UnityEngine;

public enum SdfPrimitiveKind
{
    Ellipsoid, // комната
    Capsule,   // звено туннеля, колонна
    Box        // укрытие (скруглённый параллелепипед, повёрнутый вокруг Y)
}

/// <summary>Какой штамп поставил примитив: по этому меш красится в свой цвет (StampPainter).</summary>
public enum SdfStampKind : byte
{
    None,   // комната или туннель
    Niche,
    Pillar,
    Cover
}

public enum SdfAnchorKind
{
    Incubator, // ниша под инкубатор
    Pad,       // центр ровного пятачка: ДУ на площадке, МППУ и раздатчик в хабе
    Cover,
    Pillar
}

/// <summary>Точка для расстановки (этап 7): что и где поставлено штампом.</summary>
public struct SdfAnchor
{
    public SdfAnchorKind Kind;
    public Vector3 Position; // на полу
    public Vector3 Forward;  // ниша: смотрит в комнату
    public int Node;
}

/// <summary>
/// Примитив SDF-сцены. Расстояние отрицательно внутри (в воздухе пещеры).
/// </summary>
public struct SdfPrimitive
{
    public SdfPrimitiveKind Kind;
    public Vector3 A;      // центр эллипсоида / начало капсулы
    public Vector3 B;      // конец капсулы
    public Vector3 Radii;  // радиусы эллипсоида; у капсулы радиус в x
    public bool HasFloor;
    /// <summary>Эллипсоид: высота пола (y). Капсула: доля радиуса ниже оси.</summary>
    public float Floor;
    /// <summary>Регион: 1..N — узлы графа, дальше — рёбра. 0 — камень.</summary>
    public ushort Region;
    /// <summary>Множитель шума поверхности (0 — гладко, 1 — полный шум).</summary>
    public float NoiseMask;
    /// <summary>Эллипсоид: ровный пятачок пола в центре (площадка под ДУ).</summary>
    public bool FlatFloor;
    /// <summary>Радиус ровного пятачка по горизонтали, м.</summary>
    public float PadRadius;
    /// <summary>Центр пятачка в плоскости XZ (может быть сдвинут от центра комнаты).</summary>
    public Vector2 PadCenter;
    /// <summary>Камень, а не воздух: вычитается из пещеры (колонны, укрытия).</summary>
    public bool Solid;
    /// <summary>Box: поворот вокруг Y, рад. Локальная ось X = (cos, 0, sin).</summary>
    public float Yaw;
    /// <summary>Box: радиус скругления рёбер, м.</summary>
    public float Rounding;
    /// <summary>Свой радиус smooth-min (0 — общий из настроек). Нужен нишам, чтобы не расплывались.</summary>
    public float Blend;
    /// <summary>Штамп, к которому относится примитив (None — комната или туннель).</summary>
    public SdfStampKind Stamp;
    public Bounds Bounds;

    public float Distance(Vector3 p)
    {
        if (Kind == SdfPrimitiveKind.Ellipsoid)
        {
            // Приближённое расстояние до эллипсоида (Inigo Quilez)
            Vector3 l = p - A;
            float k0 = new Vector3(l.x / Radii.x, l.y / Radii.y, l.z / Radii.z).magnitude;
            float k1 = new Vector3(l.x / (Radii.x * Radii.x), l.y / (Radii.y * Radii.y), l.z / (Radii.z * Radii.z)).magnitude;
            float d = k1 > 1e-6f ? k0 * (k0 - 1f) / k1 : -Mathf.Min(Radii.x, Mathf.Min(Radii.y, Radii.z));

            // Срез пола: всё ниже уровня пола — камень
            return HasFloor ? Mathf.Max(d, Floor - p.y) : d;
        }
        else if (Kind == SdfPrimitiveKind.Box)
        {
            Vector3 o = p - A;
            float c = Mathf.Cos(Yaw), sn = Mathf.Sin(Yaw);
            var l = new Vector3(Mathf.Abs(o.x * c + o.z * sn), Mathf.Abs(o.y), Mathf.Abs(-o.x * sn + o.z * c));
            Vector3 q = l - (Radii - Vector3.one * Rounding);
            float outside = Vector3.Max(q, Vector3.zero).magnitude;
            float inside = Mathf.Min(Mathf.Max(q.x, Mathf.Max(q.y, q.z)), 0f);
            return outside + inside - Rounding;
        }
        else
        {
            Vector3 ab = B - A;
            float len2 = Vector3.Dot(ab, ab);
            float t = len2 > 1e-8f ? Mathf.Clamp01(Vector3.Dot(p - A, ab) / len2) : 0f;
            Vector3 c = A + ab * t;
            float r = Radii.x;
            float d = (p - c).magnitude - r;

            // Пол туннеля идёт параллельно оси, ниже неё на долю радиуса
            return HasFloor ? Mathf.Max(d, c.y - r * Floor - p.y) : d;
        }
    }

    public static Bounds EllipsoidBounds(Vector3 center, Vector3 radii) => new(center, radii * 2f);

    public static Bounds BoxBounds(Vector3 center, Vector3 halfExtents)
    {
        float h = new Vector2(halfExtents.x, halfExtents.z).magnitude;
        return new Bounds(center, new Vector3(h, halfExtents.y, h) * 2f);
    }

    public static Bounds CapsuleBounds(Vector3 a, Vector3 b, float r)
    {
        var bounds = new Bounds(a, Vector3.one * (r * 2f));
        bounds.Encapsulate(new Bounds(b, Vector3.one * (r * 2f)));
        return bounds;
    }
}

/// <summary>
/// Этап 2 пайплайна: пещера как набор примитивов, слитых через smooth-min,
/// плюс шум и горизонтальный варп.
/// </summary>
public class SdfScene
{
    public readonly List<SdfPrimitive> Primitives = new();
    public readonly List<SdfAnchor> Anchors = new();
    public readonly SdfSceneSettings Settings;

    /// <summary>Число узлов графа: регионы 1..NodeCount — комнаты, дальше — туннели.</summary>
    public int NodeCount;

    private readonly Vector3 _warpOffsetX, _warpOffsetZ, _noiseOffset;

    public SdfScene(SdfSceneSettings settings, int seed)
    {
        Settings = settings;
        var rng = new System.Random(seed);
        _warpOffsetX = RandomOffset(rng);
        _warpOffsetZ = RandomOffset(rng);
        _noiseOffset = RandomOffset(rng);
    }

    /// <summary>
    /// Насколько примитив может влиять за пределами своего AABB:
    /// по этому запасу примитивы раскладываются по чанкам.
    /// </summary>
    public float InfluenceMargin =>
        Mathf.Max(Settings.blend, Mathf.Max(Settings.roomBlend, Settings.stampBlend)) + Settings.noiseAmplitude + Settings.warpAmplitude + 1f;

    public void Add(SdfPrimitive primitive) => Primitives.Add(primitive);

    public Bounds GetBounds()
    {
        if (Primitives.Count == 0) return new Bounds();
        var b = Primitives[0].Bounds;
        foreach (var p in Primitives) b.Encapsulate(p.Bounds);
        return b;
    }

    /// <summary>Регион — комната (узел графа)?</summary>
    public bool IsRoomRegion(ushort region) => region >= 1 && region <= NodeCount;
    public int RegionToNode(ushort region) => region - 1;
    public int RegionToEdge(ushort region) => region - NodeCount - 1;

    /// <summary>
    /// SDF воздуха в точке: меньше нуля — пещера, больше — камень.
    /// candidates — индексы примитивов, которые могут влиять на эту точку.
    /// label — регион примитива, который победил в smooth-min (0, если точка в камне).
    /// </summary>
    public float Evaluate(Vector3 p, List<int> candidates, out ushort label)
    {
        var s = Settings;

        // Горизонтальный варп: стены гнутся, полы остаются ровными
        Vector3 q = Warp(p);

        // Капсулы одного туннеля идут подряд и объединяются обычным min, иначе на стыках
        // smooth-min даёт вздутия. Цепочка туннеля сливается с остальным через blend,
        // лепестки комнат — через roomBlend, чтобы комната была одной плавной полостью.
        float d = 0f;
        bool hasD = false;
        float chainMin = float.MaxValue;
        ushort chainRegion = 0;
        bool hasChain = false;

        float best = float.MaxValue;
        int bestIdx = -1;

        // Ровный пятачок, над которым стоит точка. Выравнивается всё внутри круга,
        // в том числе туннели и лепестки; туннели начинаются от края пятачка, поэтому перепада нет.
        bool hasFlat = false;
        float flatFloor = 0f;

        // Маска шума смешивается по весам близости, а не берётся у победителя: иначе на границе
        // комнаты и туннеля маска скачет и на стыке получается ступенька
        float maskSum = 0f, weightSum = 0f;
        float maskK = 0.5f * Mathf.Max(0.5f, Mathf.Max(s.blend, s.roomBlend));

        // Каменные штампы: объединяются между собой и потом вычитаются из воздуха
        float solid = float.MaxValue;
        bool hasSolid = false;

        foreach (int i in candidates)
        {
            var prim = Primitives[i];
            float di = prim.Distance(q);
            if (prim.Solid)
            {
                solid = Mathf.Min(solid, di);
                hasSolid = true;
                continue;
            }

            float w = Mathf.Exp(Mathf.Clamp(-di / maskK, -60f, 60f));
            maskSum += w * prim.NoiseMask;
            weightSum += w;

            if (di < best)
            {
                best = di;
                bestIdx = i;
            }

            if (prim.FlatFloor)
            {
                float fx = q.x - prim.PadCenter.x;
                float fz = q.z - prim.PadCenter.y;
                if (fx * fx + fz * fz < prim.PadRadius * prim.PadRadius && (!hasFlat || prim.Floor > flatFloor))
                {
                    hasFlat = true;
                    flatFloor = prim.Floor;
                }
            }

            bool isCapsule = prim.Kind == SdfPrimitiveKind.Capsule;
            if (isCapsule && hasChain && prim.Region == chainRegion)
            {
                chainMin = Mathf.Min(chainMin, di);
                continue;
            }

            if (hasChain)
            {
                d = hasD ? SdfNoise.SmoothMin(d, chainMin, s.blend) : chainMin;
                hasD = true;
                hasChain = false;
            }

            if (isCapsule)
            {
                chainRegion = prim.Region;
                chainMin = di;
                hasChain = true;
            }
            else
            {
                d = hasD ? SdfNoise.SmoothMin(d, di, prim.Blend > 0f ? prim.Blend : s.roomBlend) : di;
                hasD = true;
            }
        }

        if (hasChain)
            d = hasD ? SdfNoise.SmoothMin(d, chainMin, s.blend) : chainMin;

        if (bestIdx < 0)
        {
            label = 0;
            return float.MaxValue;
        }

        // smooth-max: камень штампа плавно врастает в пол и стены
        if (hasSolid)
            d = -SdfNoise.SmoothMin(-d, solid, s.stampBlend);

        var winner = Primitives[bestIdx];
        float mask = weightSum > 0f ? maskSum / weightSum : winner.NoiseMask;

        // Над ровным полом шум у пола гасится, а выше полосы floorNoiseBand действует в полную силу
        if (hasFlat)
            mask *= Mathf.SmoothStep(0f, 1f, (q.y - flatFloor) / Mathf.Max(0.01f, s.floorNoiseBand));

        if (s.noiseAmplitude > 0f && mask > 0f)
            d += SdfNoise.Fbm(q * s.noiseFrequency + _noiseOffset) * s.noiseAmplitude * mask;

        // Под ровным полом — камень: ни smooth-min с туннелем, ни шум не продавят канаву.
        // Только в слое толщиной clampDepth, чтобы не перекрыть туннель, идущий под комнатой.
        float clampDepth = s.floorNoiseBand + s.noiseAmplitude + s.blend;
        if (hasFlat && q.y > flatFloor - clampDepth)
            d = Mathf.Max(d, flatFloor - q.y);

        label = d < 0f ? winner.Region : (ushort)0;
        return d;
    }

    /// <summary>Мировая точка → координаты примитивов (горизонтальный сдвиг варпом).</summary>
    public Vector3 Warp(Vector3 p)
    {
        var s = Settings;
        if (s.warpAmplitude <= 0f) return p;
        Vector3 wp = p * s.warpFrequency;
        p.x += SdfNoise.ValueNoise(wp + _warpOffsetX) * s.warpAmplitude;
        p.z += SdfNoise.ValueNoise(wp + _warpOffsetZ) * s.warpAmplitude;
        return p;
    }

    /// <summary>Обратно: координаты примитивов → мировая точка, где эта часть пещеры оказалась после варпа.</summary>
    public Vector3 Unwarp(Vector3 q)
    {
        Vector3 p = q;
        for (int i = 0; i < 6; i++)
        {
            Vector3 w = Warp(p);
            p.x += q.x - w.x;
            p.z += q.z - w.z;
        }
        return p;
    }

    private static Vector3 RandomOffset(System.Random rng) =>
        new(rng.Next(-10000, 10000), rng.Next(-10000, 10000), rng.Next(-10000, 10000));
}
