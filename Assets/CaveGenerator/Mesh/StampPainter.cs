using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class StampPaintSettings
{
    [Tooltip("Писать веса штампов в vertex color: R ниша, G колонна, B пятачок, A укрытие. " +
             "В материале Custom/CaveRock включи «Цвета штампов».")]
    public bool enabled = true;

    [Tooltip("Ширина перехода от ниши к стене комнаты, м.")]
    [Min(0.01f)] public float nicheFalloff = 1.2f;
    [Tooltip("Ширина перехода от колонны к полу и потолку, м.")]
    [Min(0.01f)] public float pillarFalloff = 2.5f;
    [Tooltip("Ширина перехода от укрытия к полу, м.")]
    [Min(0.01f)] public float coverFalloff = 1.2f;
    [Tooltip("Ширина мягкого края пятачка, м (половина внутрь, половина наружу).")]
    [Min(0.01f)] public float padFalloff = 1.5f;
    [Tooltip("Насколько выше и ниже пола пятачка ещё красить, м: дальше уже стена.")]
    [Min(0.01f)] public float padHeight = 0.6f;
}

public interface IVertexPainter
{
    /// <summary>Цвет каждой вершины чанка. bounds — границы вершин в локальных координатах сцены.</summary>
    Color32[] Paint(Vector3[] vertices, Bounds bounds);
}

/// <summary>
/// Красит вершины по близости к штампам SDF-сцены. Вес 1 на поверхности штампа,
/// к краю полосы спада плавно уходит в 0 — так колонна перетекает в пол.
/// Канал на каждый тип: R ниша, G колонна, B пятачок, A укрытие.
/// </summary>
public sealed class StampPainter : IVertexPainter
{
    private readonly SdfScene _scene;
    private readonly StampPaintSettings _s;
    private readonly List<int> _stamps = new(); // штампы и комнаты с ровным пятачком
    private readonly List<int> _local = new();  // те из них, что рядом с текущим чанком
    private readonly float _margin;

    public StampPainter(SdfScene scene, StampPaintSettings settings)
    {
        _scene = scene;
        _s = settings;

        for (int i = 0; i < scene.Primitives.Count; i++)
        {
            var p = scene.Primitives[i];
            if (p.Stamp != SdfStampKind.None || p.FlatFloor)
                _stamps.Add(i);
        }

        float falloff = Mathf.Max(Mathf.Max(settings.nicheFalloff, settings.pillarFalloff),
                                  Mathf.Max(settings.coverFalloff, settings.padFalloff + settings.padHeight));
        // Примитивы лежат в координатах до варпа, а вершины — после
        _margin = falloff + scene.Settings.warpAmplitude + scene.Settings.noiseAmplitude + 1f;
    }

    public Color32[] Paint(Vector3[] vertices, Bounds bounds)
    {
        var colors = new Color32[vertices.Length];

        bounds.Expand(_margin * 2f);
        _local.Clear();
        foreach (int i in _stamps)
            if (_scene.Primitives[i].Bounds.Intersects(bounds))
                _local.Add(i);

        if (_local.Count == 0)
            return colors;

        for (int v = 0; v < vertices.Length; v++)
        {
            Vector4 w = Weights(_scene.Warp(vertices[v]));
            colors[v] = new Color32(ToByte(w.x), ToByte(w.y), ToByte(w.z), ToByte(w.w));
        }
        return colors;
    }

    private Vector4 Weights(Vector3 q)
    {
        Vector4 w = Vector4.zero;
        foreach (int i in _local)
        {
            var prim = _scene.Primitives[i];
            switch (prim.Stamp)
            {
                case SdfStampKind.Niche:
                    // Ниша — воздух: внутри и на её стенках расстояние ≤ 0
                    w.x = Mathf.Max(w.x, Falloff(prim.Distance(q), _s.nicheFalloff));
                    break;
                case SdfStampKind.Pillar:
                    w.y = Mathf.Max(w.y, Falloff(prim.Distance(q), _s.pillarFalloff));
                    break;
                case SdfStampKind.Cover:
                    w.w = Mathf.Max(w.w, Falloff(prim.Distance(q), _s.coverFalloff));
                    break;
                default:
                    // Комната с пятачком: круг на полу, к краю круга и вверх по стене вес гаснет
                    float dx = q.x - prim.PadCenter.x;
                    float dz = q.z - prim.PadCenter.y;
                    float r = Mathf.Sqrt(dx * dx + dz * dz);
                    float radial = Falloff(r - prim.PadRadius + _s.padFalloff * 0.5f, _s.padFalloff);
                    float height = Falloff(Mathf.Abs(q.y - prim.Floor), _s.padHeight);
                    w.z = Mathf.Max(w.z, radial * height);
                    break;
            }
        }
        return w;
    }

    /// <summary>1 при d ≤ 0, плавно до 0 при d ≥ width.</summary>
    private static float Falloff(float d, float width) => 1f - Mathf.SmoothStep(0f, 1f, d / width);

    private static byte ToByte(float x) => (byte)Mathf.RoundToInt(Mathf.Clamp01(x) * 255f);
}
