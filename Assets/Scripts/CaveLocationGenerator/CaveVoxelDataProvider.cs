using UnityEngine;

/// <summary>
/// Генератор density field по графу пещеры.
/// 
/// Изменения:
///  - Кривые тоннели: каждое ребро = 2 сегмента (A→Mid→B)
///  - Floor-cap: пол каждого узла восстанавливается поверх
///    пересекающих тоннелей → нет разломов
///  - Абсолютная FloorDepth → нет ступенек на стыках
/// </summary>
public class CaveVoxelDataProvider : IVoxelDataProvider
{
    private readonly Vector3Int _chunkSize;
    private readonly Vector3 _worldMax;
    private readonly float _smoothK;
    private readonly float _noiseScale;
    private readonly float _noiseAmp;
    private readonly float _noiseOffset;
    private readonly float _floorDepth;
    private readonly float _ceilRatio;

    // Узлы
    private readonly Vector3[] _nodePos;
    private readonly float[] _nodeRadii;
    private readonly int _nodeCount;

    // Сегменты тоннелей (2 на ребро если есть midpoint, иначе 1)
    private readonly Vector3[] _segStart;
    private readonly Vector3[] _segDir;    // end - start
    private readonly float[] _segLenSq;
    private readonly float[] _segRadii;
    private readonly int _segCount;

    private readonly float _maxPrimRadius;

    public CaveVoxelDataProvider(CaveGraph graph, MarchingCubesConfig mcConfig, CaveGenerationConfig caveConfig)
    {
        _chunkSize = mcConfig.ChunkSize;
        _worldMax = Vector3.Scale((Vector3)mcConfig.WorldSize, (Vector3)_chunkSize);
        _smoothK = caveConfig.SmoothBlendFactor;
        _noiseScale = caveConfig.NoiseScale;
        _noiseAmp = caveConfig.NoiseAmplitude;
        _noiseOffset = caveConfig.NoiseOffset;
        _floorDepth = caveConfig.FloorDepth;
        _ceilRatio = caveConfig.CeilingHeightRatio;

        // --- Кэш узлов ---
        _nodeCount = graph.Nodes.Count;
        _nodePos = new Vector3[_nodeCount];
        _nodeRadii = new float[_nodeCount];
        float maxR = 0f;
        for (int i = 0; i < _nodeCount; i++)
        {
            _nodePos[i] = graph.Nodes[i].Position;
            _nodeRadii[i] = graph.Nodes[i].Radius;
            if (_nodeRadii[i] > maxR) maxR = _nodeRadii[i];
        }

        // --- Кэш сегментов (разбиваем рёбра с midpoint на 2) ---
        // Считаем общее количество сегментов
        int totalSegs = 0;
        foreach (var edge in graph.Edges)
            totalSegs += edge.HasMidpoint ? 2 : 1;

        _segStart = new Vector3[totalSegs];
        _segDir = new Vector3[totalSegs];
        _segLenSq = new float[totalSegs];
        _segRadii = new float[totalSegs];

        int si = 0;
        foreach (var edge in graph.Edges)
        {
            Vector3 posA = _nodePos[edge.NodeA];
            Vector3 posB = _nodePos[edge.NodeB];
            float r = edge.Radius;
            if (r > maxR) maxR = r;

            if (edge.HasMidpoint)
            {
                // Сегмент 1: A → Mid
                Vector3 dir1 = edge.Midpoint - posA;
                _segStart[si] = posA;
                _segDir[si] = dir1;
                _segLenSq[si] = Vector3.Dot(dir1, dir1);
                _segRadii[si] = r;
                si++;

                // Сегмент 2: Mid → B
                Vector3 dir2 = posB - edge.Midpoint;
                _segStart[si] = edge.Midpoint;
                _segDir[si] = dir2;
                _segLenSq[si] = Vector3.Dot(dir2, dir2);
                _segRadii[si] = r;
                si++;
            }
            else
            {
                Vector3 dir = posB - posA;
                _segStart[si] = posA;
                _segDir[si] = dir;
                _segLenSq[si] = Vector3.Dot(dir, dir);
                _segRadii[si] = r;
                si++;
            }
        }

        _segCount = totalSegs;
        _maxPrimRadius = maxR;
    }

    public float[] Generate(Vector3Int chunkCoord)
    {
        Vector3Int samples = _chunkSize + Vector3Int.one;
        int sx = samples.x, sy = samples.y, sz = samples.z;
        var data = new float[sx * sy * sz];

        float ox = chunkCoord.x * _chunkSize.x;
        float oy = chunkCoord.y * _chunkSize.y;
        float oz = chunkCoord.z * _chunkSize.z;

        if (IsChunkFarFromCave(ox, oy, oz, sx, sy, sz))
        {
            for (int i = 0; i < data.Length; i++)
                data[i] = 100f;
            return data;
        }

        for (int z = 0; z < sz; z++)
        for (int y = 0; y < sy; y++)
        for (int x = 0; x < sx; x++)
        {
            float wx = ox + x;
            float wy = oy + y;
            float wz = oz + z;

            data[x + y * sx + z * sx * sy] = IsBorder(wx, wy, wz)
                ? 100f
                : SampleCave(wx, wy, wz);
        }

        return data;
    }

    // =========================================================================
    //  SDF
    // =========================================================================

    private float SampleCave(float px, float py, float pz)
    {
        float result = float.MaxValue;

        // --- Узлы (купол + цилиндр + пол) ---
        for (int i = 0; i < _nodeCount; i++)
        {
            float d = NodeSDF(px, py, pz, i);
            result = SmoothMin(result, d, _smoothK);
        }

        // --- Сегменты тоннелей (арка + пол) ---
        for (int i = 0; i < _segCount; i++)
        {
            float d = SegmentSDF(px, py, pz, i);
            result = SmoothMin(result, d, _smoothK);
        }

        // --- Floor-cap: восстанавливаем пол под каждым узлом ---
        // Если тоннель проходит ниже узла С и пробивает его пол,
        // принудительно делаем породу под полом С.
        result = ApplyFloorCaps(px, py, pz, result);

        // --- Шум (подавлен на полу) ---
        if (_noiseAmp > 0f && result < _noiseAmp * 3f)
        {
            float noise = SampleNoise3D(px, py, pz);
            float floorFade = ComputeFloorFade(px, py, pz);
            result += noise * floorFade;
        }

        return result;
    }

    /// <summary>
    /// SDF узла: купол сверху + цилиндр + плоский пол.
    /// </summary>
    private float NodeSDF(float px, float py, float pz, int idx)
    {
        float cx = _nodePos[idx].x, cy = _nodePos[idx].y, cz = _nodePos[idx].z;
        float r = _nodeRadii[idx];

        float dx = px - cx, dz = pz - cz;
        float horizDist = Mathf.Sqrt(dx * dx + dz * dz);
        float dy = py - cy;
        float floorY = cy - _floorDepth;

        if (dy > 0f)
        {
            // Купол
            float sdy = dy / _ceilRatio;
            return Mathf.Sqrt(horizDist * horizDist + sdy * sdy) - r;
        }
        if (py >= floorY)
        {
            // Стены (цилиндр)
            return horizDist - r;
        }
        // Под полом
        return Mathf.Max(horizDist - r, floorY - py);
    }

    /// <summary>
    /// SDF сегмента тоннеля: арочный профиль вдоль линии.
    /// </summary>
    private float SegmentSDF(float px, float py, float pz, int idx)
    {
        Vector3 start = _segStart[idx];
        Vector3 dir = _segDir[idx];
        float lenSq = _segLenSq[idx];
        float r = _segRadii[idx];

        float apx = px - start.x, apy = py - start.y, apz = pz - start.z;
        float dot = apx * dir.x + apy * dir.y + apz * dir.z;
        float t = Mathf.Clamp01(dot / lenSq);

        float axisX = start.x + dir.x * t;
        float axisY = start.y + dir.y * t;
        float axisZ = start.z + dir.z * t;

        float hdx = px - axisX, hdz = pz - axisZ;
        float horizDist = Mathf.Sqrt(hdx * hdx + hdz * hdz);

        float dy = py - axisY;
        float floorY = axisY - _floorDepth;

        if (dy > 0f)
        {
            float sdy = dy / _ceilRatio;
            return Mathf.Sqrt(horizDist * horizDist + sdy * sdy) - r;
        }
        if (py >= floorY)
        {
            return horizDist - r;
        }
        return Mathf.Max(horizDist - r, floorY - py);
    }

    /// <summary>
    /// Восстанавливает пол под каждым узлом.
    /// Если точка горизонтально внутри радиуса узла,
    /// но ниже его пола — принудительно делаем породу.
    /// Это предотвращает разломы от пересекающих тоннелей.
    /// </summary>
    private float ApplyFloorCaps(float px, float py, float pz, float currentSDF)
    {
        for (int i = 0; i < _nodeCount; i++)
        {
            float cx = _nodePos[i].x, cz = _nodePos[i].z;
            float r = _nodeRadii[i];

            float dx = px - cx, dz = pz - cz;
            float horizDistSq = dx * dx + dz * dz;

            // Только для точек горизонтально внутри узла (с запасом на smooth)
            float capRadius = r + _smoothK * 0.5f;
            if (horizDistSq > capRadius * capRadius) continue;

            float floorY = _nodePos[i].y - _floorDepth;

            // Если точка ниже пола этого узла — должна быть порода
            if (py < floorY)
            {
                // Плавный переход вместо резкого обрезания
                float belowDist = floorY - py;
                // SDF пола: положительное = порода, плавно нарастает вниз
                float floorSDF = belowDist;

                // Берём максимум: либо текущий SDF (порода), либо floor cap
                // max делает "твёрже" — восстанавливает породу
                currentSDF = Mathf.Max(currentSDF, floorSDF - r * 0.5f);
            }
        }

        return currentSDF;
    }

    /// <summary>
    /// Коэффициент подавления шума на полу.
    /// 0 = пол (шума нет), 1 = стена/потолок (полный шум).
    /// </summary>
    private float ComputeFloorFade(float px, float py, float pz)
    {
        float minFade = 1f;
        float fadeZone = 2f;

        for (int i = 0; i < _nodeCount; i++)
        {
            float cx = _nodePos[i].x, cz = _nodePos[i].z, r = _nodeRadii[i];
            float dx = px - cx, dz = pz - cz;
            if (dx * dx + dz * dz > (r + _smoothK) * (r + _smoothK)) continue;

            float floorY = _nodePos[i].y - _floorDepth;
            float above = py - floorY;
            if (above >= 0f && above < fadeZone)
            {
                float fade = above / fadeZone;
                if (fade < minFade) minFade = fade;
            }
        }

        // Также проверяем сегменты
        for (int i = 0; i < _segCount; i++)
        {
            Vector3 s = _segStart[i];
            Vector3 dir = _segDir[i];
            float lenSq = _segLenSq[i];
            float r = _segRadii[i];

            float apx = px - s.x, apy = py - s.y, apz = pz - s.z;
            float dot = apx * dir.x + apy * dir.y + apz * dir.z;
            float t = Mathf.Clamp01(dot / lenSq);

            float axisX = s.x + dir.x * t;
            float axisY = s.y + dir.y * t;
            float axisZ = s.z + dir.z * t;

            float hdx = px - axisX, hdz = pz - axisZ;
            if (hdx * hdx + hdz * hdz > (r + _smoothK) * (r + _smoothK)) continue;

            float floorY = axisY - _floorDepth;
            float above = py - floorY;
            if (above >= 0f && above < fadeZone)
            {
                float fade = above / fadeZone;
                if (fade < minFade) minFade = fade;
            }
        }

        return minFade;
    }

    // =========================================================================
    //  Утилиты
    // =========================================================================

    private static float SmoothMin(float a, float b, float k)
    {
        if (k <= 0f) return Mathf.Min(a, b);
        float h = Mathf.Max(k - Mathf.Abs(a - b), 0f) / k;
        return Mathf.Min(a, b) - h * h * k * 0.25f;
    }

    private float SampleNoise3D(float x, float y, float z)
    {
        float s = _noiseScale, off = _noiseOffset;
        float n1 = Mathf.PerlinNoise(x * s + off, y * s + off);
        float n2 = Mathf.PerlinNoise(y * s + off + 137.3f, z * s + off + 137.3f);
        float n3 = Mathf.PerlinNoise(z * s + off + 274.6f, x * s + off + 274.6f);
        return ((n1 + n2 + n3) / 3f - 0.5f) * 2f * _noiseAmp;
    }

    private bool IsBorder(float x, float y, float z)
    {
        return x <= 1f || x >= _worldMax.x - 1f
            || y <= 1f || y >= _worldMax.y - 1f
            || z <= 1f || z >= _worldMax.z - 1f;
    }

    private bool IsChunkFarFromCave(float ox, float oy, float oz, int sx, int sy, int sz)
    {
        float cx = ox + sx * 0.5f, cy = oy + sy * 0.5f, cz = oz + sz * 0.5f;
        float chunkR = Mathf.Sqrt(sx * sx + sy * sy + sz * sz) * 0.5f;
        float threshold = chunkR + _maxPrimRadius + _noiseAmp + _smoothK + _floorDepth + 2f;
        float threshSq = threshold * threshold;

        for (int i = 0; i < _nodeCount; i++)
        {
            float dx = cx - _nodePos[i].x, dy = cy - _nodePos[i].y, dz = cz - _nodePos[i].z;
            if (dx * dx + dy * dy + dz * dz < threshSq) return false;
        }

        for (int i = 0; i < _segCount; i++)
        {
            Vector3 s = _segStart[i];
            Vector3 dir = _segDir[i];
            float apx = cx - s.x, apy = cy - s.y, apz = cz - s.z;
            float dot = apx * dir.x + apy * dir.y + apz * dir.z;
            float t = Mathf.Clamp01(dot / _segLenSq[i]);
            float px = s.x + dir.x * t - cx, py = s.y + dir.y * t - cy, pz = s.z + dir.z * t - cz;
            if (px * px + py * py + pz * pz < threshSq) return false;
        }

        return true;
    }
}