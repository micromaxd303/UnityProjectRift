using System;
using System.Collections.Generic;
using UnityEngine;

public class CaveGenerator : MonoBehaviour
{
    [Header("Config")]
    [SerializeField] private MarchingCubesConfig config;
    [SerializeField] private MeshBuilderConfig MBconfig;
    [SerializeField] private MitchelConfig mitchelConfig;

    [SerializeField] private ComputeShader marchingCubesShader;

    [Header("Culling")]
    [SerializeField] private bool enableCulling = true;

    private ChunkCuller _culler;
    private IMarchingCubesBackend _backend;
    private IVoxelDataProvider _voxelDataProvider;
    private MeshBuilder _meshBuilder;
    private Dictionary<Vector3Int, GameObject> _chunkObjects = new();
    private SettingsChange _pending;
    
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

        if (Input.GetKeyDown(KeyCode.H))
        {
            GraphBuilder.BuildGraph(config, mitchelConfig, gameObject.transform.position);
        }

        ProcessPending();
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
}