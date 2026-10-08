using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Этап 3 пайплайна: вокселизация SDF-сцены по чанкам.
/// Плотность > 0 — камень, < 0 — воздух (как в VoxelDataGenerator).
/// Каждый чанк считает только примитивы, чей AABB его касается; чанки без примитивов —
/// сплошной камень, бэкенды MC их пропускают. Вместе с плотностью сохраняется метка региона.
/// </summary>
public class SdfVoxelDataProvider : IVoxelDataProvider, ISparseVoxelDataProvider
{
    private const float SolidDensity = 100f;

    private readonly SdfScene _scene;
    private readonly Vector3Int _chunkSize;
    private readonly Vector3Int _worldChunks;
    private readonly Vector3Int _worldMax; // размер мира в вокселях
    private readonly float _voxelSize;
    private readonly Vector3 _halfWorld;

    private readonly Dictionary<Vector3Int, List<int>> _chunkPrimitives = new();
    private readonly Dictionary<Vector3Int, ushort[]> _labels = new();

    /// <summary>Метки регионов по чанкам (только для непустых чанков).</summary>
    public IReadOnlyDictionary<Vector3Int, ushort[]> Labels => _labels;
    public SdfScene Scene => _scene;
    public int ActiveChunkCount => _chunkPrimitives.Count;

    public SdfVoxelDataProvider(SdfScene scene, MarchingCubesConfig config)
    {
        _scene = scene;
        _chunkSize = config.ChunkSize;
        _worldChunks = config.WorldSize;
        _worldMax = Vector3Int.Scale(_worldChunks, _chunkSize);
        _voxelSize = config.VoxelSize;
        _halfWorld = config.TotalWorldSize * 0.5f;

        DistributePrimitives();
    }

    public bool IsSolidChunk(Vector3Int chunkCoord) => !_chunkPrimitives.ContainsKey(chunkCoord);

    public float[] Generate(Vector3Int chunkCoord)
    {
        Vector3Int samples = _chunkSize + Vector3Int.one;
        int sx = samples.x, sy = samples.y, sz = samples.z;
        var data = new float[sx * sy * sz];

        if (!_chunkPrimitives.TryGetValue(chunkCoord, out var primitives))
        {
            for (int i = 0; i < data.Length; i++) data[i] = SolidDensity;
            return data;
        }

        var labels = new ushort[data.Length];
        Vector3Int origin = Vector3Int.Scale(chunkCoord, _chunkSize);

        for (int z = 0; z < sz; z++)
        for (int y = 0; y < sy; y++)
        for (int x = 0; x < sx; x++)
        {
            int idx = x + y * sx + z * sx * sy;
            Vector3Int v = origin + new Vector3Int(x, y, z);

            if (IsBorder(v))
            {
                data[idx] = SolidDensity;
                continue;
            }

            float d = _scene.Evaluate(VoxelToLocal(v), primitives, out ushort label);
            data[idx] = d / _voxelSize; // в вокселях, как и у VoxelDataGenerator
            labels[idx] = label;
        }

        _labels[chunkCoord] = labels;
        return data;
    }

    /// <summary>Регион в точке (локальные координаты CaveGenerator). 0 — камень или чанк не посчитан.</summary>
    public ushort GetRegion(Vector3 localPos)
    {
        Vector3 vf = (localPos + _halfWorld) / _voxelSize;
        var v = new Vector3Int(Mathf.RoundToInt(vf.x), Mathf.RoundToInt(vf.y), Mathf.RoundToInt(vf.z));
        if (v.x < 0 || v.y < 0 || v.z < 0 || v.x > _worldMax.x || v.y > _worldMax.y || v.z > _worldMax.z)
            return 0;

        // Сэмплы на границе чанка есть в обоих соседях; берём чанк с меньшей координатой
        var chunk = new Vector3Int(
            Mathf.Min(v.x / _chunkSize.x, _worldChunks.x - 1),
            Mathf.Min(v.y / _chunkSize.y, _worldChunks.y - 1),
            Mathf.Min(v.z / _chunkSize.z, _worldChunks.z - 1));

        if (!_labels.TryGetValue(chunk, out var labels))
            return 0;

        Vector3Int l = v - Vector3Int.Scale(chunk, _chunkSize);
        int sx = _chunkSize.x + 1, sy = _chunkSize.y + 1;
        return labels[l.x + l.y * sx + l.z * sx * sy];
    }

    /// <summary>Непустые чанки (в которых есть хотя бы один примитив).</summary>
    public IEnumerable<Vector3Int> ActiveChunks => _chunkPrimitives.Keys;

    /// <summary>Считает чанк и записывает плотность с метками в общее поле. Метки чанка здесь больше не хранятся.</summary>
    public void FillChunk(Vector3Int coord, VoxelField field)
    {
        float[] data = Generate(coord);
        _labels.TryGetValue(coord, out var labels);
        field.WriteChunk(coord, data, labels);
        _labels.Remove(coord);
    }

    /// <summary>Этап 3 целиком: все непустые чанки в поле. Остальное поле — камень.</summary>
    public void FillField(VoxelField field)
    {
        foreach (var coord in ActiveChunks)
            FillChunk(coord, field);
    }

    /// <summary>
    /// Ремонт туннелей: капсулы указанных регионов становятся шире в inflate раз, пол остаётся
    /// на той же высоте, шум на них выключается. Примитивы меняются на месте, чтобы капсулы
    /// туннеля шли подряд (на этом держится слияние цепочки в SdfScene.Evaluate).
    /// Возвращает чанки, которые надо пересчитать.
    /// </summary>
    public HashSet<Vector3Int> InflateRegions(ICollection<ushort> regions, float inflate)
    {
        var changed = new List<int>();
        for (int i = 0; i < _scene.Primitives.Count; i++)
        {
            var p = _scene.Primitives[i];
            if (p.Kind != SdfPrimitiveKind.Capsule || !regions.Contains(p.Region)) continue;

            float r = p.Radii.x;
            float nr = r * inflate;
            if (p.HasFloor)
            {
                // Пол капсулы: ось - r * Floor. Ось поднимается так, чтобы пол не сдвинулся
                var lift = new Vector3(0f, (nr - r) * p.Floor, 0f);
                p.A += lift;
                p.B += lift;
            }
            p.Radii = new Vector3(nr, nr, nr);
            p.NoiseMask = 0f;
            p.Bounds = SdfPrimitive.CapsuleBounds(p.A, p.B, nr);
            _scene.Primitives[i] = p;
            changed.Add(i);
        }

        _chunkPrimitives.Clear();
        DistributePrimitives();

        var affected = new HashSet<Vector3Int>();
        foreach (var kv in _chunkPrimitives)
            foreach (int i in changed)
                if (kv.Value.Contains(i))
                {
                    affected.Add(kv.Key);
                    break;
                }
        return affected;
    }

    // ------------------------------------------------------------------

    /// <summary>Раскладка AABB примитивов по чанкам с запасом на blend, шум и варп.</summary>
    private void DistributePrimitives()
    {
        float margin = _scene.InfluenceMargin;
        Vector3 chunkWorld = (Vector3)_chunkSize * _voxelSize;

        for (int i = 0; i < _scene.Primitives.Count; i++)
        {
            Bounds b = _scene.Primitives[i].Bounds;
            b.Expand(margin * 2f);

            Vector3 min = (b.min + _halfWorld);
            Vector3 max = (b.max + _halfWorld);
            var cMin = new Vector3Int(
                Mathf.Max(0, Mathf.FloorToInt(min.x / chunkWorld.x)),
                Mathf.Max(0, Mathf.FloorToInt(min.y / chunkWorld.y)),
                Mathf.Max(0, Mathf.FloorToInt(min.z / chunkWorld.z)));
            var cMax = new Vector3Int(
                Mathf.Min(_worldChunks.x - 1, Mathf.FloorToInt(max.x / chunkWorld.x)),
                Mathf.Min(_worldChunks.y - 1, Mathf.FloorToInt(max.y / chunkWorld.y)),
                Mathf.Min(_worldChunks.z - 1, Mathf.FloorToInt(max.z / chunkWorld.z)));

            for (int x = cMin.x; x <= cMax.x; x++)
            for (int y = cMin.y; y <= cMax.y; y++)
            for (int z = cMin.z; z <= cMax.z; z++)
            {
                var coord = new Vector3Int(x, y, z);
                if (!_chunkPrimitives.TryGetValue(coord, out var list))
                    _chunkPrimitives[coord] = list = new List<int>();
                list.Add(i);
            }
        }
    }

    private Vector3 VoxelToLocal(Vector3Int v) => (Vector3)v * _voxelSize - _halfWorld;

    private bool IsBorder(Vector3Int v) =>
        v.x <= 0 || v.x >= _worldMax.x ||
        v.y <= 0 || v.y >= _worldMax.y ||
        v.z <= 0 || v.z >= _worldMax.z;
}
