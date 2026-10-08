using System;
using System.Collections.Generic;
using CaveGraphBuilder;
using UnityEngine;

public class CaveGenerator : MonoBehaviour
{
    [Header("Config")]
    [SerializeField] private MarchingCubesConfig config;
    [SerializeField] private GraphBuilderConfig graphBuilderConfig;
    [SerializeField] private MeshBuilderConfig MBconfig;

    [SerializeField] private ComputeShader marchingCubesShader;

    [Header("Culling")]
    [SerializeField] private bool enableCulling = true;

    private ChunkCuller _culler;
    private IMarchingCubesBackend _backend;
    private IVoxelDataProvider _voxelDataProvider;
    private MeshBuilder _meshBuilder;
    private Dictionary<Vector3Int, GameObject> _chunkObjects = new();
    private SettingsChange _pending;
    private CaveGraphResult _graphResult;
    private LevelValidationResult _validation;
    // SDF-сцена последней генерации: из неё гизмо берёт точки штампов
    private SdfScene _scene;
    // Сид, с которым построен текущий граф (после перестройки отличается от Seed запроса)
    private int _builtSeed;
    // Запрос текущего уровня (копия, правки в инспекторе на него не влияют)
    private LevelRequest _request;

    /// <summary>Последний построенный уровень (этап 8). null, пока пещера не построена из графа.</summary>
    public LevelContext Context { get; private set; }
    /// <summary>Уровень построен: меш готов, LevelContext заполнен.</summary>
    public event Action<LevelContext> LevelGenerated;
    // Клетки пола для гизмо: позиция и достижимость от спавна
    private readonly List<Vector3> _walkCells = new();
    private readonly List<bool> _walkReached = new();

    [Header("Graph test (временно, до LevelRequest)")]
    [Tooltip("Запрос для клавиши H. Игра вызывает GenerateLevel(request) со своим запросом.")]
    [SerializeField] private LevelRequest testRequest = new();

    [Header("SDF scene")]
    [Tooltip("Вкл: пещера строится из графа (клавиша H). Выкл или графа нет: демо-сцена VoxelDataGenerator.")]
    [SerializeField] private bool useGraphScene = true;
    [Tooltip("После удачного построения графа сразу строить меш.")]
    [SerializeField] private bool buildMeshAfterGraph = true;
    [SerializeField] private SdfSceneSettings sdfSettings = new();

    [Header("Цвета штампов (материал Custom/CaveRock)")]
    [SerializeField] private StampPaintSettings stampPaint = new();

    [Header("Проверка уровня (этап 4)")]
    [SerializeField] private LevelAnalysisSettings analysisSettings = new();

    [Header("Расстановка (этап 7)")]
    [Tooltip("Работает только для уровня, прошедшего проверку этапа 4.")]
    [SerializeField] private PlacementSettings placementSettings = new();

    [Header("Debug")]
    [SerializeField] private bool showDelaunayEdges = false;
    [SerializeField] private bool showRegionRadius = true;
    [SerializeField] private bool showLabels = true;
    [Tooltip("Клетки пола, на которых можно стоять: зелёные — достижимы от старта, красные — нет.")]
    [SerializeField] private bool showWalkable = false;
    [Tooltip("Рисовать каждую n-ю клетку пола по X и Z, чтобы гизмо не тормозило.")]
    [SerializeField, Min(1)] private int walkableGizmoStride = 2;
    [Tooltip("Точки штампов: ниши инкубаторов, пятачки, укрытия, колонны.")]
    [SerializeField] private bool showAnchors = true;
    [Tooltip("Разломы (фиолетовые ромбы, лучи к точкам выхода), точки начальных врагов (красные), кристаллы на потолке (голубые круги).")]
    [SerializeField] private bool showPlacement = true;

    private void Awake()
    {
        _meshBuilder = new MeshBuilder(MBconfig, transform);
    }
    
    private void OnEnable()
    {
        _backend = CreateBackend();
        _backend.Initialize(config);
        MBconfig.Changed += OnSettingsChanged;
    }

    private void OnDisable()
    {
        MBconfig.Changed -= OnSettingsChanged;
        _backend?.Dispose();
        _backend = null;
    }

    private void OnDestroy()
    {
        _meshBuilder?.Dispose();
        _meshBuilder = null;
    }
    private void OnSettingsChanged(SettingsChange change) => _pending |= change;

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.G))
            _pending |= SettingsChange.Extract;

        /*if (Input.GetKeyDown(KeyCode.H))
        {
            GraphBuilder.BuildGraph(config, mitchelConfig, gameObject.transform.position);
        }*/
        
        if (Input.GetKeyDown(KeyCode.H))
        {
            GenerateGraph(testRequest);
        }

        ProcessPending();
    }
    
    /// <summary>
    /// Этапы 0–8 сразу: граф, проверка, меш, LevelContext. Возвращает null, если граф не построился.
    /// </summary>
    public LevelContext GenerateLevel(LevelRequest request)
    {
        if (!GenerateGraph(request))
            return null;
        // Меш строим сразу, а не в следующем кадре: снимаем отложенную перестройку от GenerateGraph
        _pending &= ~SettingsChange.Extract;
        Generate();
        return Context;
    }

    private bool GenerateGraph(LevelRequest request)
    {
        _request = request.Clone();
        Context = null;
        _graphResult = BuildGraph(_request.Seed);
        _builtSeed = _request.Seed;
        _validation = null;
        _scene = null;
        _walkCells.Clear();
        _walkReached.Clear();

        string log = string.Join("\n", _graphResult.Log);
        if (_graphResult.Success)
        {
            Debug.Log($"Граф построен за {_graphResult.Attempts} попыт.\n{log}");
            if (useGraphScene && buildMeshAfterGraph)
                _pending |= SettingsChange.Extract;
            return true;
        }

        Debug.LogWarning($"Граф не построен за {_graphResult.Attempts} попыт.\n{log}");
        return false;
    }

    private CaveGraphResult BuildGraph(int seed)
    {
        return new CaveGraphGenerator(graphBuilderConfig).Generate(_request.ToRequirements(), GetBorderDelta(), seed);
    }
    
    // Узлы ставятся с отступом от края мира на радиус самой большой комнаты, чтобы комнаты не срезались границей
    private Vector2 GetBorderDelta()
    {
        float margin = 5f + (graphBuilderConfig != null ? graphBuilderConfig.caveGraphSettings.largeRadius.y : 0f);
        return new Vector2(
            config.WorldSize.x * config.ChunkSize.x,
            config.WorldSize.z * config.ChunkSize.z) * config.VoxelSize / 2 - new Vector2(margin, margin);
    }
    
    // Вся тяжёлая работа - здесь, а не в обработчике события
    // (он может прийти из OnValidate, где создавать/удалять объекты нельзя).
    private void ProcessPending()
    {
        if (_pending == SettingsChange.None)
            return;

        SettingsChange change = _pending;
        _pending = SettingsChange.None;

        if ((change & SettingsChange.Extract) != 0)
            Generate();
        else if ((change & SettingsChange.PostProcess) != 0)
            _meshBuilder.Reprocess();

        if ((change & SettingsChange.Render) != 0)
            _meshBuilder.ApplyRenderSettings();

        if ((change & SettingsChange.Physics) != 0)
            _meshBuilder.ApplyPhysicsSettings();

        if ((change & SettingsChange.Memory) != 0)
            _meshBuilder.ReleaseSourceData();
    }

    public void Generate()
    {
        if (_backend == null)
        {
            Debug.LogWarning("CaveGenerator выключен, генерация невозможна.");
            return;
        }
        var timer = System.Diagnostics.Stopwatch.StartNew();

        // Провайдер пересоздаётся, чтобы подхватить изменения MarchingCubesConfig (seed и т.п.).
        _voxelDataProvider = CreateVoxelDataProvider();
        var meshes = _backend.GenerateMesh(_voxelDataProvider);

        // Демо-сцена штампов не знает, поэтому красим только пещеру из SDF-сцены
        bool paint = stampPaint.enabled && _scene != null && !(_voxelDataProvider is VoxelDataGenerator);
        _meshBuilder.Painter = paint ? new StampPainter(_scene, stampPaint) : null;

        _chunkObjects = _meshBuilder.Build(meshes);

        if (enableCulling)
        {
            if (_culler == null)
                _culler = gameObject.AddComponent<ChunkCuller>();
            _culler.RegisterChunks(_chunkObjects);
        }

        Debug.Log($"Generated {_chunkObjects.Count} chunks за {timer.ElapsedMilliseconds} мс");

        BuildContext();
    }

    /// <summary>Этап 8: итог для игры. Только для пещеры из графа, у демо-сцены нет регионов.</summary>
    private void BuildContext()
    {
        Context = null;
        if (_voxelDataProvider is VoxelDataGenerator || _graphResult == null || !_graphResult.Success || _request == null)
            return;

        Context = LevelContextBuilder.Build(_request, _request.ToRequirements(), _graphResult.Graph, _builtSeed,
            _scene, _validation, transform);

        if (placementSettings.enabled && _validation != null && _validation.Success)
        {
            var placeLog = LevelPlacer.Place(Context, _validation, _scene, placementSettings, _builtSeed);
            Debug.Log("Расстановка: " + string.Join("\n", placeLog));
        }

        var main = Context.MainObjective;
        Debug.Log($"LevelContext: {Context.Request.MainMission}, сид {Context.Seed}, " +
                  $"проверен: {(Context.Validated ? "да" : "нет")}, регионов {Context.Regions.Count}, " +
                  $"целей {Context.Objectives.Count} (основная: регионов {main?.Regions.Count ?? 0}, мест {main?.Anchors.Count ?? 0}), " +
                  $"~{Context.EstimatedDurationSeconds / 60f:F1} мин");
        LevelGenerated?.Invoke(Context);
    }

    private IVoxelDataProvider CreateVoxelDataProvider()
    {
        if (!useGraphScene || _graphResult == null || !_graphResult.Success)
            return new VoxelDataGenerator(config);

        if (analysisSettings.enabled)
            return CreateValidatedProvider();

        var scene = SdfSceneBuilder.Build(_graphResult.Graph, sdfSettings, graphBuilderConfig.movement, _builtSeed);
        var provider = new SdfVoxelDataProvider(scene, config);
        _scene = scene;
        WarnIfOutsideWorld(scene);

        Debug.Log($"SDF-сцена: {scene.Primitives.Count} примитивов, " +
                  $"непустых чанков {provider.ActiveChunkCount} из " +
                  $"{config.WorldSize.x * config.WorldSize.y * config.WorldSize.z}");
        return provider;
    }

    /// <summary>
    /// Этапы 2–4 с проверкой: если уровень непроходим и ремонт туннелей не помог,
    /// строится новый граф с соседним сидом (Seed + 1, + 2, ...).
    /// </summary>
    private IVoxelDataProvider CreateValidatedProvider()
    {
        var movement = graphBuilderConfig.movement;
        LevelValidationResult last = null;
        int baseSeed = _builtSeed;

        for (int k = 0; k < analysisSettings.maxTopologyAttempts; k++)
        {
            int seed = baseSeed + k;
            if (k > 0)
            {
                var graph = BuildGraph(seed);
                if (!graph.Success)
                {
                    Debug.LogWarning($"Перестройка графа, сид {seed}: граф не построен");
                    continue;
                }
                _graphResult = graph;
            }

            if (k > 0) _builtSeed = seed;

            var v = LevelValidator.Run(_graphResult.Graph, config, sdfSettings, analysisSettings, movement, seed);
            last = v;
            string log = string.Join("\n", v.Log);

            if (v.Success)
            {
                Debug.Log($"Уровень прошёл проверку (сид графа {seed}, ремонтов {v.Repairs}).\n{log}");
                if (seed != _request.Seed)
                    Debug.Log($"Граф заменён: чтобы повторить этот уровень, поставь Seed = {seed} в запросе");
                break;
            }
            Debug.LogWarning($"Уровень не прошёл проверку (сид графа {seed}).\n{log}");
        }

        if (last == null)
            return new VoxelDataGenerator(config);

        _validation = last;
        _scene = last.Scene;
        if (!last.Success)
            Debug.LogWarning($"За {analysisSettings.maxTopologyAttempts} попыт. проходимый уровень не получился. " +
                             "Меш построен по последней попытке, непроходимые места — красным в гизмо (showWalkable).");

        WarnIfOutsideWorld(last.Scene);
        CacheWalkGizmo(last);
        return new VoxelFieldProvider(last.Field);
    }

    private void CacheWalkGizmo(LevelValidationResult v)
    {
        _walkCells.Clear();
        _walkReached.Clear();
        var a = v.Analysis;
        if (a?.Standable == null) return;

        var f = v.Field;
        int stride = Mathf.Max(1, walkableGizmoStride);
        for (int i = 0; i < a.Standable.Length; i++)
        {
            if (!a.Standable[i]) continue;
            var c = f.Coord(i);
            if (c.x % stride != 0 || c.z % stride != 0) continue;
            _walkCells.Add(f.ToLocal(i));
            _walkReached.Add(a.Reachable != null && a.Reachable[i]);
        }
    }

    private void WarnIfOutsideWorld(SdfScene scene)
    {
        // Пока размер мира задаётся MarchingCubesConfig, а не границами графа: предупреждаем, если пещера не влезает
        Bounds caveBounds = scene.GetBounds();
        Bounds world = new Bounds(Vector3.zero, config.TotalWorldSize);
        if (!world.Contains(caveBounds.min) || !world.Contains(caveBounds.max))
            Debug.LogWarning($"Пещера ({caveBounds.min} – {caveBounds.max}) выходит за границы мира " +
                             $"({world.min} – {world.max}), края будут срезаны камнем. " +
                             "Уменьши heightAmplitude/радиусы комнат или увеличь WorldSize.");
    }
    
    [ContextMenu("Clear Cave")]
    public void Clear()
    {
        _meshBuilder?.Clear();

        /*if (_culler != null)
            _culler.Clear();*/

        _chunkObjects.Clear();
        Context = null;
        _validation = null;
        _scene = null;
        _walkCells.Clear();
        _walkReached.Clear();

        (_voxelDataProvider as IDisposable)?.Dispose();
        _voxelDataProvider = null;

        _pending = SettingsChange.None; // отложенные изменения к пустой сцене неприменимы
    }

    private IMarchingCubesBackend CreateBackend()
    {
        var platform = new PlatformProvider();
        var selector = new BackendSelector(platform);
        var recommendation = selector.Recommend(config);

        Debug.Log($"Backend: {recommendation.BackendType} | " +
                  $"API: {recommendation.GraphicsAPI} | " +
                  $"Reason: {recommendation.Reason}");

        if (recommendation.BackendType == MarchingCubesConfig.BackendType.ComputeShader)
            return new ComputeShaderBackend(marchingCubesShader);

        return new JobSystemBackend();
    }
    
    private void OnDrawGizmosSelected()
    {
        if (config == null || graphBuilderConfig == null) return;

        Vector2 border = GetBorderDelta();
        float amp = graphBuilderConfig.mitchelSamplerSettings.heightAmplitude;

        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = Color.magenta;
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(border.x * 2f, amp * 2f, border.y * 2f));
        Gizmos.matrix = Matrix4x4.identity;

        DrawWalkable();
        DrawAnchors();
        DrawPlacement();

        var graph = _graphResult?.Graph;
        if (graph == null) return;

        Vector3 W(int i) => transform.TransformPoint(graph.Nodes[i].Position);

        if (showDelaunayEdges && _graphResult.DelaunayEdges != null)
        {
            Gizmos.color = new Color(0.5f, 0.5f, 0.5f, 0.35f);
            foreach (var (a, b) in _graphResult.DelaunayEdges)
                Gizmos.DrawLine(W(a), W(b));
        }

        var failed = _validation?.Analysis?.FailedEdges;
        var unreachable = _validation?.Analysis?.UnreachableNodes;

        for (int ei = 0; ei < graph.Edges.Count; ei++)
        {
            var e = graph.Edges[ei];
            Gizmos.color = failed != null && failed.Contains(ei) ? Color.red : EdgeColor(e);
            Gizmos.DrawLine(W(e.A), W(e.B));

            // у обрыва отмечаем нижний конец: туда можно только спрыгнуть
            if (e.Type == EdgeType.Drop)
            {
                int low = graph.Nodes[e.A].Position.y < graph.Nodes[e.B].Position.y ? e.A : e.B;
                Vector3 to = W(low), from = W(e.Other(low));
                Gizmos.DrawSphere(Vector3.Lerp(from, to, 0.85f), 0.6f);
            }
        }

        for (int i = 0; i < graph.Nodes.Count; i++)
        {
            var node = graph.Nodes[i];
            Vector3 world = W(i);

            Gizmos.color = RoleColor(node.Role);
            Gizmos.DrawSphere(world, 1f);
            if (showRegionRadius)
                Gizmos.DrawWireSphere(world, node.Radius);
            if (unreachable != null && unreachable.Contains(i))
            {
                Gizmos.color = Color.red;
                Gizmos.DrawWireSphere(world, node.Radius + 1f);
            }

#if UNITY_EDITOR
            if (showLabels)
            {
                string label = node.Role == LevelInfo.RegionRole.None
                    ? $"{i}  d={node.Distance01:F2}"
                    : $"{i} {node.Role}  d={node.Distance01:F2}  вх={graph.Degree(i)}";
                UnityEditor.Handles.Label(world + Vector3.up * 1.5f, label);
            }
#endif
        }
    }

    private void DrawWalkable()
    {
        if (!showWalkable || _walkCells.Count == 0 || config == null) return;
        float s = config.VoxelSize * Mathf.Max(1, walkableGizmoStride) * 0.8f;
        var size = new Vector3(s, 0.05f, s);
        var reached = new Color(0.2f, 1f, 0.3f, 0.6f);
        var lost = new Color(1f, 0.15f, 0.15f, 0.8f);

        Gizmos.matrix = transform.localToWorldMatrix;
        for (int i = 0; i < _walkCells.Count; i++)
        {
            Gizmos.color = _walkReached[i] ? reached : lost;
            Gizmos.DrawCube(_walkCells[i], size);
        }
        Gizmos.matrix = Matrix4x4.identity;
    }

    private void DrawPlacement()
    {
        if (!showPlacement || Context == null) return;

        // Точки контекста локальные: рисуем в пространстве пещеры, чтобы они двигались вместе с ней
        Gizmos.matrix = transform.localToWorldMatrix;

        var riftColor = new Color(0.7f, 0.2f, 1f);
        foreach (var rift in Context.Rifts)
        {
            Gizmos.color = riftColor;
            // ромб: вертикальная «щель» разрыва
            Vector3 c = rift.Position;
            Vector3 up = Vector3.up * 0.9f, side = Vector3.right * 0.35f, fwd = Vector3.forward * 0.35f;
            Gizmos.DrawLine(c + up, c + side); Gizmos.DrawLine(c + side, c - up);
            Gizmos.DrawLine(c - up, c - side); Gizmos.DrawLine(c - side, c + up);
            Gizmos.DrawLine(c + up, c + fwd); Gizmos.DrawLine(c + fwd, c - up);
            Gizmos.DrawLine(c - up, c - fwd); Gizmos.DrawLine(c - fwd, c + up);

            Gizmos.color = new Color(riftColor.r, riftColor.g, riftColor.b, 0.5f);
            foreach (var g in rift.GroundPoints)
            {
                Gizmos.DrawLine(c, g);
                Gizmos.DrawCube(g + Vector3.up * 0.05f, new Vector3(0.6f, 0.1f, 0.6f));
            }
        }

        Gizmos.color = new Color(1f, 0.2f, 0.2f);
        foreach (var region in Context.Regions)
            foreach (var p in region.SpawnPoints)
                Gizmos.DrawSphere(p + Vector3.up * 0.4f, 0.4f);

        // Места под объекты миссий из этапа 7
        foreach (var a in Context.Anchors)
        {
            switch (a.Kind)
            {
                case LevelAnchorKind.CrystalVein:
                    Gizmos.color = new Color(0.6f, 0.2f, 1f);
                    Gizmos.DrawCube(a.Position, Vector3.one * 0.5f);
                    Gizmos.DrawLine(a.Position, a.Position + a.Forward * 1.5f);
                    break;
                case LevelAnchorKind.Item:
                    Gizmos.color = new Color(1f, 0.85f, 0.3f);
                    Gizmos.DrawCube(a.Position + Vector3.up * 0.4f, Vector3.one * 0.8f);
                    break;
                case LevelAnchorKind.Lair:
                case LevelAnchorKind.Evacuation:
                case LevelAnchorKind.Arena:
                    Gizmos.color = a.Kind == LevelAnchorKind.Lair ? new Color(0.6f, 0f, 0f)
                        : a.Kind == LevelAnchorKind.Evacuation ? new Color(0.2f, 1f, 1f) : new Color(1f, 0.3f, 0.6f);
                    Gizmos.DrawSphere(a.Position + Vector3.up * 0.5f, 0.5f);
                    Gizmos.DrawWireSphere(a.Position + Vector3.up * 0.5f, a.Radius);
                    break;
            }
        }

        // Кристаллы на потолке: круг допустимого основания и линия по нормали
        Gizmos.color = new Color(0.3f, 1f, 1f);
        foreach (var c in Context.CeilingPoints)
        {
            Gizmos.DrawLine(c.Position, c.Position + c.Normal * Mathf.Min(c.MaxLength, 1.5f));
            Vector3 t1 = Vector3.Cross(c.Normal, Vector3.forward);
            if (t1.sqrMagnitude < 1e-4f) t1 = Vector3.Cross(c.Normal, Vector3.right);
            t1 = t1.normalized * c.MaxRadius;
            Vector3 t2 = Vector3.Cross(c.Normal, t1).normalized * c.MaxRadius;
            Vector3 prev = c.Position + t1;
            for (int k = 1; k <= 12; k++)
            {
                float a = k * Mathf.PI * 2f / 12f;
                Vector3 next = c.Position + t1 * Mathf.Cos(a) + t2 * Mathf.Sin(a);
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }
        Gizmos.matrix = Matrix4x4.identity;
    }

    private void DrawAnchors()
    {
        if (!showAnchors || _scene == null) return;

        foreach (var a in _scene.Anchors)
        {
            Vector3 p = transform.TransformPoint(a.Position);
            switch (a.Kind)
            {
                case SdfAnchorKind.Incubator:
                    Gizmos.color = Color.red;
                    Gizmos.DrawCube(p + Vector3.up * 0.75f, new Vector3(1f, 1.5f, 1f));
                    Gizmos.DrawLine(p + Vector3.up * 0.75f, p + Vector3.up * 0.75f + transform.TransformDirection(a.Forward) * 2.5f);
                    break;
                case SdfAnchorKind.Pad:
                    Gizmos.color = Color.yellow;
                    Gizmos.DrawWireCube(p + Vector3.up * 0.05f, new Vector3(2f, 0.1f, 2f));
                    break;
                case SdfAnchorKind.Cover:
                    Gizmos.color = new Color(1f, 0.6f, 0.2f);
                    Gizmos.DrawWireSphere(p + Vector3.up * 0.5f, 0.5f);
                    break;
                case SdfAnchorKind.Pillar:
                    Gizmos.color = Color.gray;
                    Gizmos.DrawLine(p, p + Vector3.up * 6f);
                    break;
            }
        }
    }

    private static Color EdgeColor(CaveEdge e) => e.Type switch
    {
        EdgeType.Drop => new Color(1f, 0.55f, 0f),
        EdgeType.Crawl => Color.cyan,
        _ => e.IsLoop ? new Color(0.6f, 1f, 0.6f) : Color.green
    };

    private static Color RoleColor(LevelInfo.RegionRole role) => role switch
    {
        LevelInfo.RegionRole.Start => Color.white,
        LevelInfo.RegionRole.Nest => Color.red,
        LevelInfo.RegionRole.Hub => Color.blue,
        LevelInfo.RegionRole.Deposit => new Color(0.6f, 0.2f, 1f),
        LevelInfo.RegionRole.Platform => Color.yellow,
        LevelInfo.RegionRole.Arena => new Color(1f, 0.3f, 0.6f),
        LevelInfo.RegionRole.Evacuation => new Color(0.2f, 1f, 1f),
        LevelInfo.RegionRole.Cache => new Color(1f, 0.85f, 0.3f),
        LevelInfo.RegionRole.Lair => new Color(0.6f, 0f, 0f),
        _ => Color.gray
    };
}
