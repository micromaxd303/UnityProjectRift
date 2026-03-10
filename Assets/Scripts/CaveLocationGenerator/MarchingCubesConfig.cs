using UnityEngine;

[CreateAssetMenu(fileName = "MarchingCubesConfig", menuName = "Scriptable Objects/MarchingCubesConfig")]
public class MarchingCubesConfig : ScriptableObject
{
    [field: SerializeField, Header("Voxel Settings")] 
    public Vector3Int WorldSize { get; private set; } = new Vector3Int(8, 8, 8);
    [field: SerializeField] public Vector3Int ChunkSize { get; private set; } = new Vector3Int(4, 4, 4);
    [field: SerializeField] public float VoxelSize { get; private set; } = 1f;
    
    [field: SerializeField, Range(-30,20)]
    public float SurfaceLevel { get; private set; } = 0f;
    
    [field: SerializeField, Header("Backend Settings")]
    public BackendType PreferredBackend { get; private set; } = BackendType.Auto;
    
    [field: SerializeField] public GraphicsAPIType PreferredGraphicsAPI { get; private set; } = GraphicsAPIType.Auto;

    public int TotalChunkVoxels => ChunkSize.x * ChunkSize.y * ChunkSize.z;

    public int RecommendationThreshold = 16348;
    
    public enum BackendType { Auto, ComputeShader, JobSystem, }
    public enum GraphicsAPIType { Auto, DX11, DX12, Vulkan }

    private void OnValidate()
    {
        WorldSize = Vector3Int.Max(WorldSize, Vector3Int.one * 2) ;
        ChunkSize = Vector3Int.Max(ChunkSize, Vector3Int.one);
        VoxelSize = Mathf.Max(VoxelSize, 0.01f);
    }
}