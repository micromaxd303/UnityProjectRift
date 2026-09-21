using System.Collections.Generic;
using UnityEngine;

public class CaveGenerator : MonoBehaviour
{
    [Header("Config")]
    [SerializeField] private MarchingCubesConfig config;
    [SerializeField] private ComputeShader marchingCubesShader;
    [SerializeField] private Material caveMaterial;

    [Header("Debug")]
    [SerializeField] private bool drawGizmos;
    
    [Header("Culling")]
    [SerializeField] private bool enableCulling = true;

    private ChunkCuller _culler;
    private IMarchingCubesBackend _backend;
    private IVoxelDataProvider _voxelDataProvider;
    private MeshBuilder _meshBuilder;
    private Dictionary<Vector3Int, GameObject> _chunkObjects = new();
    private bool _isGenerated;

    private void Start()
    {
        _voxelDataProvider = new VoxelDataGenerator(config);
        _meshBuilder = new MeshBuilder(caveMaterial, transform);
        _backend = CreateBackend();
        _backend.Initialize(config);
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.G))
        {
            Generate();
        }
    }

    public void Generate()
    {
        ClearExisting();

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

    private void ClearExisting()
    {
        foreach (var (_, go) in _chunkObjects)
        {
            if (go != null)
                Destroy(go);
        }

        _chunkObjects.Clear();
        _isGenerated = false;
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

    private void OnDisable()
    {
        _backend?.Dispose();
    }

    private void OnDestroy()
    {
        ClearExisting();
        _backend?.Dispose();
    }
}