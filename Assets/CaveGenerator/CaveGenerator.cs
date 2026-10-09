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
    
    
    private List<Vector3> _nodes;
    
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
            GenerateGraph();
        }

        ProcessPending();
    }
    
    private void GenerateGraph()
    {
        MitchelSampler nodePositionGenerator = new MitchelSampler(graphBuilderConfig.mitchelSamplerSettings, 1);
        _nodes = nodePositionGenerator.GenerateNode(GetBorderDelta());
    }
    
    private Vector2 GetBorderDelta() =>
        new Vector2(
            config.WorldSize.x * config.ChunkSize.x,
            config.WorldSize.z * config.ChunkSize.z) * config.VoxelSize / 2 - new Vector2(15f, 15f);
    
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
        // Провайдер пересоздаётся, чтобы подхватить изменения MarchingCubesConfig (seed и т.п.).
        _voxelDataProvider = new VoxelDataGenerator(config);
        var meshes = _backend.GenerateMesh(_voxelDataProvider);
        
        _chunkObjects = _meshBuilder.Build(meshes);

        if (enableCulling)
        {
            if (_culler == null)
                _culler = gameObject.AddComponent<ChunkCuller>();
            _culler.RegisterChunks(_chunkObjects);
        }

        Debug.Log($"Generated {_chunkObjects.Count} chunks");
    }
    
    [ContextMenu("Clear Cave")]
    public void Clear()
    {
        _meshBuilder?.Clear();

        /*if (_culler != null)
            _culler.Clear();*/

        _chunkObjects.Clear();

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

        if (_nodes == null) return;

        for (int i = 0; i < _nodes.Count; i++)
        {
            Vector3 world = transform.TransformPoint(_nodes[i]);

            float t = _nodes.Count > 1 ? i / (float)(_nodes.Count - 1) : 0f;
            Gizmos.color = Color.Lerp(Color.red, Color.green, t);
            Gizmos.DrawSphere(world, 1f);

#if UNITY_EDITOR
            UnityEditor.Handles.Label(world + Vector3.up * 1.5f, i.ToString());
#endif
        }
    }
}