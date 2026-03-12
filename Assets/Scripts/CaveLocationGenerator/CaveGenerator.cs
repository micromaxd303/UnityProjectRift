using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class CaveGenerator : MonoBehaviour
{
    [Header("Config")]
    [SerializeField] private MarchingCubesConfig config;
    [SerializeField] private CaveGenerationConfig caveConfig;
    [SerializeField] private ComputeShader marchingCubesShader;
    [SerializeField] private Material caveMaterial;

    [Header("Generation")]
    [SerializeField] private int seed;
    [SerializeField] private bool randomizeSeed = true;

    [Header("Culling")]
    [SerializeField] private bool enableCulling = true;

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;
    [SerializeField] private bool drawNodeLabels = true;

    private ChunkCuller _culler;
    private IMarchingCubesBackend _backend;
    private IVoxelDataProvider _voxelDataProvider;
    private MeshBuilder _meshBuilder;
    private Dictionary<Vector3Int, GameObject> _chunkObjects = new();
    private bool _isGenerated;

    /// <summary>Граф текущей пещеры.</summary>
    public CaveGraph CurrentGraph { get; private set; }

    /// <summary>
    /// Смещение между voxel-space и мировыми координатами чанков.
    /// Автоматически вычисляется после генерации.
    /// </summary>
    private Vector3 _voxelToWorldOffset;

    private void Start()
    {
        _meshBuilder = new MeshBuilder(caveMaterial, transform);
        _backend = CreateBackend();
        _backend.Initialize(config);
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.G))
            Generate();
    }

    public void Generate()
    {
        ClearExisting();

        if (randomizeSeed)
            seed = Random.Range(int.MinValue, int.MaxValue);

        Vector3 worldSizeVoxels = Vector3.Scale(
            (Vector3)config.WorldSize,
            (Vector3)config.ChunkSize
        );

        CurrentGraph = CaveGraphBuilder.Build(caveConfig, worldSizeVoxels, seed);
        Debug.Log($"[CaveGenerator] Graph: {CurrentGraph.Nodes.Count} nodes, " +
                  $"{CurrentGraph.Edges.Count} edges, " +
                  $"{CurrentGraph.ObjectiveIndices.Count} objectives (seed: {seed})");

        _voxelDataProvider = new CaveVoxelDataProvider(CurrentGraph, config, caveConfig);

        var meshes = _backend.GenerateMesh(_voxelDataProvider);
        _chunkObjects = _meshBuilder.Build(meshes);

        // Автоопределение смещения voxel→world
        DetectVoxelToWorldOffset();

        if (enableCulling)
        {
            if (_culler == null)
                _culler = gameObject.AddComponent<ChunkCuller>();
            _culler.RegisterChunks(_chunkObjects);
        }

        _isGenerated = true;
        Debug.Log($"[CaveGenerator] Generated {_chunkObjects.Count} chunks | " +
                  $"voxel→world offset: {_voxelToWorldOffset}");
    }

    /// <summary>
    /// Вычисляет смещение между voxel-координатами и реальными позициями чанков.
    /// Берём любой чанк, сравниваем ожидаемую voxel-позицию с фактической.
    /// </summary>
    private void DetectVoxelToWorldOffset()
    {
        _voxelToWorldOffset = Vector3.zero;

        if (_chunkObjects.Count == 0) return;

        // Берём первый чанк
        var first = _chunkObjects.First();
        Vector3Int chunkCoord = first.Key;
        GameObject chunkGO = first.Value;

        if (chunkGO == null) return;

        // Ожидаемая позиция в voxel-space
        Vector3 expectedVoxelPos = new Vector3(
            chunkCoord.x * _chunkSize.x,
            chunkCoord.y * _chunkSize.y,
            chunkCoord.z * _chunkSize.z
        );

        // Фактическая мировая позиция
        Vector3 actualWorldPos = chunkGO.transform.position;

        _voxelToWorldOffset = actualWorldPos - expectedVoxelPos;
    }

    private Vector3Int _chunkSize => config.ChunkSize;

    /// <summary>Конвертация из voxel-space в мировые координаты.</summary>
    private Vector3 VoxelToWorld(Vector3 voxelPos)
    {
        return voxelPos + _voxelToWorldOffset;
    }

    public bool IsGenerated() => _isGenerated;

    public Vector3 GetSpawnPosition()
    {
        if (CurrentGraph == null || CurrentGraph.SpawnIndex < 0)
            return transform.position;
        return NodeFloorWorld(CurrentGraph.Nodes[CurrentGraph.SpawnIndex]);
    }

    public Vector3 GetExtractPosition()
    {
        if (CurrentGraph == null || CurrentGraph.ExtractIndex < 0)
            return transform.position;
        return NodeFloorWorld(CurrentGraph.Nodes[CurrentGraph.ExtractIndex]);
    }

    public List<Vector3> GetObjectivePositions()
    {
        var result = new List<Vector3>();
        if (CurrentGraph == null) return result;
        foreach (int idx in CurrentGraph.ObjectiveIndices)
            result.Add(NodeFloorWorld(CurrentGraph.Nodes[idx]));
        return result;
    }

    private Vector3 NodeFloorWorld(CaveNode node)
    {
        float floorY = node.Position.y - caveConfig.FloorDepth + 1f;
        return VoxelToWorld(new Vector3(node.Position.x, floorY, node.Position.z));
    }

    private void ClearExisting()
    {
        foreach (var (_, go) in _chunkObjects)
            if (go != null) Destroy(go);
        _chunkObjects.Clear();
        _isGenerated = false;
        CurrentGraph = null;
        _voxelToWorldOffset = Vector3.zero;
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

    private void OnDisable() => _backend?.Dispose();
    private void OnDestroy()
    {
        ClearExisting();
        _backend?.Dispose();
    }

    // =========================================================================
    //  Gizmos
    // =========================================================================

#if UNITY_EDITOR
    private static readonly Color CSpawn     = new Color(0f, 1f, 0.53f, 0.9f);
    private static readonly Color CExtract   = new Color(1f, 0.2f, 0.4f, 0.9f);
    private static readonly Color CObjective = new Color(1f, 0.67f, 0f, 0.9f);
    private static readonly Color CHall      = new Color(0.78f, 1f, 0f, 0.5f);
    private static readonly Color CTunnel    = new Color(0.5f, 0.5f, 0.5f, 0.25f);
    private static readonly Color CDeadend   = new Color(0.7f, 0.7f, 0.7f, 0.4f);
    private static readonly Color CEdgeMST   = new Color(1f, 1f, 1f, 0.2f);
    private static readonly Color CEdgeExtra = new Color(0.4f, 0.8f, 1f, 0.1f);

    private void OnDrawGizmos()
    {
        if (!drawGizmos || CurrentGraph == null) return;

        var graph = CurrentGraph;
        float floorDepth = caveConfig != null ? caveConfig.FloorDepth : 2.5f;

        // --- Рёбра (кривые) ---
        foreach (var edge in graph.Edges)
        {
            Vector3 a = VoxelToWorld(graph.Nodes[edge.NodeA].Position);
            Vector3 b = VoxelToWorld(graph.Nodes[edge.NodeB].Position);
            Gizmos.color = edge.IsMST ? CEdgeMST : CEdgeExtra;

            if (edge.HasMidpoint)
            {
                Vector3 mid = VoxelToWorld(edge.Midpoint);
                Gizmos.DrawLine(a, mid);
                Gizmos.DrawLine(mid, b);
                // Маркер midpoint
                Gizmos.DrawWireSphere(mid, 0.5f);
            }
            else
            {
                Gizmos.DrawLine(a, b);
            }
        }

        // --- Узлы ---
        for (int i = 0; i < graph.Nodes.Count; i++)
        {
            var node = graph.Nodes[i];
            Vector3 wp = VoxelToWorld(node.Position);
            Color col = NodeColor(node.Type);
            Gizmos.color = col;

            Gizmos.DrawWireSphere(wp, node.Radius);

            // Кольцо пола
            float floorY = wp.y - floorDepth;
            DrawWireCircle(new Vector3(wp.x, floorY, wp.z), node.Radius, col * 0.5f, 24);

            if (drawNodeLabels)
            {
                string label = node.Type switch
                {
                    CaveNodeType.Spawn     => "SPAWN",
                    CaveNodeType.Extract   => "EXTRACT",
                    CaveNodeType.Objective => $"OBJ {graph.ObjectiveIndices.IndexOf(i) + 1}",
                    CaveNodeType.Hall      => $"Hall ({node.Degree})",
                    CaveNodeType.Deadend   => "Dead",
                    _                      => null
                };

                if (label != null)
                {
                    UnityEditor.Handles.color = col;
                    UnityEditor.Handles.Label(
                        wp + Vector3.up * (node.Radius + 1.5f),
                        label,
                        new GUIStyle(GUI.skin.label)
                        {
                            normal = { textColor = col },
                            fontStyle = FontStyle.Bold,
                            fontSize = 11
                        }
                    );
                }
            }
        }
    }

    private static void DrawWireCircle(Vector3 center, float radius, Color color, int segments)
    {
        Gizmos.color = color;
        float step = 360f / segments;
        Vector3 prev = center + new Vector3(radius, 0f, 0f);
        for (int i = 1; i <= segments; i++)
        {
            float angle = i * step * Mathf.Deg2Rad;
            Vector3 next = center + new Vector3(
                Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            Gizmos.DrawLine(prev, next);
            prev = next;
        }
    }

    private static Color NodeColor(CaveNodeType type) => type switch
    {
        CaveNodeType.Spawn     => CSpawn,
        CaveNodeType.Extract   => CExtract,
        CaveNodeType.Objective => CObjective,
        CaveNodeType.Hall      => CHall,
        CaveNodeType.Deadend   => CDeadend,
        _                      => CTunnel
    };
#endif
}