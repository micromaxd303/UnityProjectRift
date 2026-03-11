using System;
using System.Collections.Generic;
using UnityEngine;

public class CaveGenerator : MonoBehaviour
{
    public MarchingCubesConfig config;
    
    public ComputeShader shader;
    
    public Material material;
    
    private IPlatformProvider _platformProvider;
    private BackendSelector _backendSelector;
    private IVoxelDataProvider _voxelDataProvider;
    
    private IMarchingCubesBackend back;
    
    
    private MeshBuilder _meshBuilder;
    
    

    private Dictionary<Vector3Int, float[]> _chunks = new();

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        _platformProvider = new PlatformProvider();
        
        _backendSelector = new BackendSelector(_platformProvider);

        _voxelDataProvider = new VoxelDataGenerator(config);


        //back = new ComputeShaderBackend(shader);

        back = new JobSystemBackend();
        
        back.Initialize(config);

        _meshBuilder = new MeshBuilder(material, gameObject.transform);
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            BackendRecommendation bckRec = _backendSelector.Recommend(config);
        
        
            Debug.Log(bckRec.BackendType);
            Debug.Log(bckRec.GraphicsAPI);
            Debug.Log(bckRec.Reason);
            Debug.Log("----");
        }

        if (Input.GetKeyDown(KeyCode.G))
        {
            _chunks.Clear();
        
            for (int x = 0; x < config.WorldSize.x; x++)
            for (int y = 0; y < config.WorldSize.y; y++)
            for (int z = 0; z < config.WorldSize.z; z++)
            {
                var coord = new Vector3Int(x, y, z);
                _chunks[coord] = _voxelDataProvider.Generate(coord);
            }


            var meshes = back.GenerateMesh(_voxelDataProvider);

            _meshBuilder.Build(meshes);
            
            
            back.Dispose();
        }
    }

    private void OnDisable()
    {
        back.Dispose();
    }

    private void OnDrawGizmos()
    {
        foreach (var (coord, data) in _chunks)
        {
            VoxelDebug.DrawGizmos(data, coord, config.ChunkSize, config.SurfaceLevel);
        }
    }
}
