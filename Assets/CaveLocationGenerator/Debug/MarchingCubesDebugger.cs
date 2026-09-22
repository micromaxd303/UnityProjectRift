using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Тонкий компонент-хост: хранит настройки в инспекторе и вызывает отрисовку
/// из контекста OnDrawGizmos (Gizmos/Handles работают только отсюда).
/// Вся логика — в статическом MarchingCubesDebug, его можно дёргать откуда
/// угодно, например прямо из CaveGenerator.OnDrawGizmos:
///     MarchingCubesDebug.DrawAll(config, transform, options);
/// </summary>
public class MarchingCubesDebugger : MonoBehaviour
{
    [SerializeField] private MarchingCubesConfig config;

    [Tooltip("Рисовать только когда объект выделен (разгружает Scene View)")]
    [SerializeField] private bool onlyWhenSelected = false;

    [SerializeField] private MarchingCubesDebug.Options options = new MarchingCubesDebug.Options();

    private void OnDrawGizmos()
    {
        if (!onlyWhenSelected) MarchingCubesDebug.DrawAll(config, transform, options);
    }

    private void OnDrawGizmosSelected()
    {
        if (onlyWhenSelected) MarchingCubesDebug.DrawAll(config, transform, options);
    }

    private void OnValidate() => MarchingCubesDebug.Invalidate();
}

/// <summary>
/// Статическая библиотека отрисовки дебага Marching Cubes.
/// Кэширует плотности (переиспользует VoxelDataGenerator) и рисует:
/// общий куб зоны, кубы чанков, сэмпл-точки со значениями и точки
/// пересечения изоповерхности (= вершины, которые поставит шейдер).
///
/// Координаты повторяют реальный пайплайн (ComputeShaderBackend + .compute):
/// вершина = globalVoxelCoord + ChunkOffset, где
/// ChunkOffset = coord*ChunkSize - worldSize/2. То есть меш ЦЕНТРИРОВАН в нуле
/// родителя и измеряется в ВОКСЕЛЯХ. VoxelSize в пайплайне сейчас не
/// используется — applyVoxelSize по умолчанию выключен.
/// </summary>
public static class MarchingCubesDebug
{
    public enum SliceAxis { None, X, Y, Z }
    public enum LabelMode { None, All, NearSurface }
    public enum PointShape { WireSphere, WireCube }

    [System.Serializable]
    public class Options
    {
        [Header("Что рисовать")]
        public bool drawWorldBounds = true;
        public bool drawChunkBounds = true;
        public bool drawPoints = true;
        [Tooltip("Точки пересечения изоповерхности на рёбрах = вершины MC")]
        public bool drawSurfaceCrossings = false;

        [Header("Соответствие реальному мешу")]
        [Tooltip("Сдвиг -worldSize/2 как в ComputeShaderBackend (меш центрирован)")]
        public bool matchBackendCentering = true;
        [Tooltip("Умножать позиции на VoxelSize. Включай ТОЛЬКО если сам масштабируешь меш — сейчас пайплайн этого не делает")]
        public bool applyVoxelSize = false;

        [Header("Фильтр точек")]
        public SliceAxis slice = SliceAxis.None;
        [Tooltip("Индекс среза в СЭМПЛАХ (вокселях), не в чанках")]
        [Min(0)] public int sliceIndex = 0;
        public bool nearSurfaceOnly = false;
        [Min(0f)] public float nearSurfaceBand = 1f;

        [Header("Вид точек")]
        public PointShape pointShape = PointShape.WireSphere;
        [Min(0f)] public float pointRadius = 0.12f;
        [Tooltip("density >= SurfaceLevel")] public Color solidColor = new Color(0.2f, 0.8f, 1f);
        [Tooltip("density < SurfaceLevel")] public Color airColor = new Color(1f, 0.35f, 0.35f);
        [Tooltip("Гасить прозрачность вдали от поверхности — оболочка станет видна")]
        public bool fadeByDistanceToSurface = true;
        [Min(0.01f)] public float fadeRange = 30f;

        [Header("Подписи (только в редакторе)")]
        public LabelMode labelMode = LabelMode.None;
        [Min(0f)] public float labelMaxDistance = 12f;
        public string labelFormat = "F1";

        [Header("Цвета границ")]
        public Color worldBoundsColor = new Color(1f, 0.9f, 0f, 1f);
        public Color chunkBoundsColor = new Color(1f, 1f, 1f, 0.22f);
        public Color crossingColor = new Color(0.2f, 1f, 0.4f, 1f);

        [Header("Защита")]
        [Tooltip("Выше этого числа точки/подписи не рисуются (только границы)")]
        public int maxPointsToDraw = 200000;
    }

    // --- кэш плотностей (глобальная сетка сэмплов) ---
    private static float[] _densities;
    private static Vector3Int _gridSize;            // = WorldSize*ChunkSize + 1
    private static MarchingCubesConfig _cachedConfig;
    private static Vector3Int _cw, _cc;
    private static bool _valid;

    public static void Invalidate() => _valid = false;

    public static void DrawAll(MarchingCubesConfig config, Transform space, Options o)
    {
        if (config == null || o == null) return;

        Vector3 worldVox = Vector3.Scale(config.WorldSize, config.ChunkSize);
        Vector3 offset = o.matchBackendCentering ? -worldVox * 0.5f : Vector3.zero;
        float scale = o.applyVoxelSize ? config.VoxelSize : 1f;

        // voxel-space -> display: (p + offset) * scale, затем трансформ объекта.
        // Всё ниже рисуется в СЫРЫХ воксельных координатах — матрица делает остальное.
        Matrix4x4 voxelToDisplay = Matrix4x4.TRS(offset * scale, Quaternion.identity, Vector3.one * scale);
        Matrix4x4 m = (space != null ? space.localToWorldMatrix : Matrix4x4.identity) * voxelToDisplay;

        Matrix4x4 prev = Gizmos.matrix;
        Gizmos.matrix = m;

        if (o.drawWorldBounds) DrawWorldBounds(config, m, o);
        if (o.drawChunkBounds) DrawChunkBounds(config, o);

        bool needGrid = o.drawPoints || o.drawSurfaceCrossings || o.labelMode != LabelMode.None;
        if (needGrid && EnsureGrid(config)) DrawSamples(config, m, o);

        Gizmos.matrix = prev;
    }

    // ------------------------------------------------------------------ границы

    private static void DrawWorldBounds(MarchingCubesConfig config, Matrix4x4 m, Options o)
    {
        Vector3 sizeVox = Vector3.Scale(config.WorldSize, config.ChunkSize);
        Gizmos.color = o.worldBoundsColor;
        Gizmos.DrawWireCube(sizeVox * 0.5f, sizeVox);

#if UNITY_EDITOR
        var ws = config.WorldSize;
        long chunks = (long)ws.x * ws.y * ws.z;
        Handles.color = o.worldBoundsColor;
        Handles.Label(
            m.MultiplyPoint3x4(new Vector3(0f, sizeVox.y + 1f, 0f)),
            $"World {ws.x}x{ws.y}x{ws.z} ({chunks} chunks)\n" +
            $"Chunk {config.ChunkSize.x}x{config.ChunkSize.y}x{config.ChunkSize.z}\n" +
            $"Voxel {config.VoxelSize} | Apply: {o.applyVoxelSize}"
        );


#endif
    }

    private static void DrawChunkBounds(MarchingCubesConfig config, Options o)
    {
        var ws = config.WorldSize; var cs = config.ChunkSize;
        Gizmos.color = o.chunkBoundsColor;
        for (int cz = 0; cz < ws.z; cz++)
        for (int cy = 0; cy < ws.y; cy++)
        for (int cx = 0; cx < ws.x; cx++)
        {
            Vector3 min = new Vector3(cx * cs.x, cy * cs.y, cz * cs.z);
            Gizmos.DrawWireCube(min + (Vector3)cs * 0.5f, cs);
        }
    }

    // ------------------------------------------------------- точки / пересечения

    private static void DrawSamples(MarchingCubesConfig config, Matrix4x4 m, Options o)
    {
        float surface = config.SurfaceLevel;
        int gx = _gridSize.x, gy = _gridSize.y, gz = _gridSize.z;
        long total = (long)gx * gy * gz;

        if (total > o.maxPointsToDraw)
        {
#if UNITY_EDITOR
            Handles.color = Color.red;
            Handles.Label(m.MultiplyPoint3x4(Vector3.zero),
                $"Точек {total} > лимита {o.maxPointsToDraw}: рисую только границы.\n" +
                "Используй Slice или подними maxPointsToDraw.");
#endif
            return;
        }

        Camera cam = Camera.current; // камера Scene View во время отрисовки гизмо

        for (int z = 0; z < gz; z++)
        for (int y = 0; y < gy; y++)
        for (int x = 0; x < gx; x++)
        {
            if (!OnSlice(o, x, y, z, gx, gy, gz)) continue;

            float d = _densities[Index(x, y, z)];
            bool nearSurface = Mathf.Abs(d - surface) <= o.nearSurfaceBand;
            bool show = !(o.nearSurfaceOnly && !nearSurface);
            Vector3 p = new Vector3(x, y, z);

            if (o.drawPoints && show)
            {
                Color c = d >= surface ? o.solidColor : o.airColor;
                if (o.fadeByDistanceToSurface)
                    c.a = Mathf.Lerp(1f, 0.1f, Mathf.Clamp01(Mathf.Abs(d - surface) / o.fadeRange));
                Gizmos.color = c;

                if (o.pointShape == PointShape.WireSphere)
                    Gizmos.DrawWireSphere(p, o.pointRadius);
                else
                    Gizmos.DrawWireCube(p, Vector3.one * (o.pointRadius * 2f));
            }

#if UNITY_EDITOR
            if (o.labelMode != LabelMode.None && show)
            {
                bool want = o.labelMode == LabelMode.All ||
                            (o.labelMode == LabelMode.NearSurface && nearSurface);
                if (want)
                {
                    Vector3 wp = m.MultiplyPoint3x4(p);
                    if (cam == null || Vector3.Distance(cam.transform.position, wp) <= o.labelMaxDistance)
                    {
                        Handles.color = d >= surface ? o.solidColor : o.airColor;
                        Handles.Label(wp, d.ToString(o.labelFormat));
                    }
                }
            }
#endif

            if (o.drawSurfaceCrossings)
            {
                TryCrossing(o, surface, x, y, z, x + 1, y, z, gx, gy, gz);
                TryCrossing(o, surface, x, y, z, x, y + 1, z, gx, gy, gz);
                TryCrossing(o, surface, x, y, z, x, y, z + 1, gx, gy, gz);
            }
        }
    }

    private static void TryCrossing(Options o, float surface,
        int x0, int y0, int z0, int x1, int y1, int z1, int gx, int gy, int gz)
    {
        if (x1 >= gx || y1 >= gy || z1 >= gz) return;

        float a = _densities[Index(x0, y0, z0)] - surface;
        float b = _densities[Index(x1, y1, z1)] - surface;
        if ((a < 0f) == (b < 0f)) return; // нет смены знака — ребро не пересекает поверхность

        float t = a / (a - b); // = (surface - d0) / (d1 - d0), как в InterpolateVertex шейдера
        Vector3 cp = Vector3.Lerp(new Vector3(x0, y0, z0), new Vector3(x1, y1, z1), t);

        Gizmos.color = o.crossingColor;
        Gizmos.DrawWireSphere(cp, o.pointRadius * 0.7f);
    }

    // ------------------------------------------------------------------ сетка

    private static bool OnSlice(Options o, int x, int y, int z, int gx, int gy, int gz)
    {
        switch (o.slice)
        {
            case SliceAxis.X: return x == Mathf.Clamp(o.sliceIndex, 0, gx - 1);
            case SliceAxis.Y: return y == Mathf.Clamp(o.sliceIndex, 0, gy - 1);
            case SliceAxis.Z: return z == Mathf.Clamp(o.sliceIndex, 0, gz - 1);
            default: return true;
        }
    }

    private static int Index(int x, int y, int z) =>
        x + y * _gridSize.x + z * _gridSize.x * _gridSize.y;

    /// <summary>Строит глобальную сетку плотностей через VoxelDataGenerator (один источник истины).</summary>
    private static bool EnsureGrid(MarchingCubesConfig config)
    {
        var ws = config.WorldSize; var cs = config.ChunkSize;
        if (_valid && _densities != null && _cachedConfig == config && ws == _cw && cs == _cc)
            return true;

        _gridSize = Vector3Int.Scale(ws, cs) + Vector3Int.one;
        long total = (long)_gridSize.x * _gridSize.y * _gridSize.z;
        if (total <= 0 || total > 50_000_000) { _densities = null; return false; }

        _densities = new float[total];
        var provider = new VoxelDataGenerator(config);
        int sx = cs.x + 1, sy = cs.y + 1, sz = cs.z + 1;

        for (int cz = 0; cz < ws.z; cz++)
        for (int cy = 0; cy < ws.y; cy++)
        for (int cx = 0; cx < ws.x; cx++)
        {
            // граничные сэмплы соседних чанков совпадают по позиции и значению —
            // перезапись безвредна и заодно дедуплицирует общие грани
            float[] local = provider.Generate(new Vector3Int(cx, cy, cz));
            for (int z = 0; z < sz; z++)
            for (int y = 0; y < sy; y++)
            for (int x = 0; x < sx; x++)
                _densities[Index(cx * cs.x + x, cy * cs.y + y, cz * cs.z + z)]
                    = local[x + y * sx + z * sx * sy];
        }

        _cachedConfig = config; _cw = ws; _cc = cs; _valid = true;
        return true;
    }
}
