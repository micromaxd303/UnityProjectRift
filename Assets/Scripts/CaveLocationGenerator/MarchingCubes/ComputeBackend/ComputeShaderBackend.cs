using System.Collections.Generic;
using UnityEngine;

public class ComputeShaderBackend : IMarchingCubesBackend
{
    private ComputeShader _computeShader;
    private MarchingCubesConfig _config;

    private int _kernel;
    private Vector3Int _samplesPerAxis;
    private int _totalSamples;
    private int _maxTriangles;

    private ComputeBuffer _voxelBuffer;
    private ComputeBuffer _triangleBuffer;
    private ComputeBuffer _countBuffer;
    private struct Triangle
    {
        public Vector3 VertexA;
        public Vector3 VertexB;
        public Vector3 VertexC;
    }
    
    public ComputeShaderBackend(ComputeShader shader)
    {
        _computeShader = shader;
    }

    public void Initialize(MarchingCubesConfig config)
    {
        _config = config;
        _samplesPerAxis = config.ChunkSize + Vector3Int.one;
        _totalSamples = _samplesPerAxis.x * _samplesPerAxis.y * _samplesPerAxis.z;
        
        // Максимум 5 треугольников на куб
        int totalCubes = config.ChunkSize.x * config.ChunkSize.y * config.ChunkSize.z;
        _maxTriangles = totalCubes * 5;

        _kernel = _computeShader.FindKernel("MarchingCubes");

        // Буфер вокселей — переиспользуется каждый чанк
        _voxelBuffer = new ComputeBuffer(_totalSamples, sizeof(float));

        // AppendStructuredBuffer — stride = 3 * float3 = 36 bytes
        _triangleBuffer = new ComputeBuffer(
            _maxTriangles, sizeof(float) * 9, ComputeBufferType.Append);

        // Счётчик для AppendBuffer
        _countBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.Raw);

        // Привязываем буферы к шейдеру
        _computeShader.SetBuffer(_kernel, "VoxelData", _voxelBuffer);
        _computeShader.SetBuffer(_kernel, "Triangles", _triangleBuffer);

        // Константы, которые не меняются между чанками
        _computeShader.SetFloat("IsoLevel", config.SurfaceLevel);
        _computeShader.SetInts("SamplesPerAxis", 
            _samplesPerAxis.x, _samplesPerAxis.y, _samplesPerAxis.z);
    }

    public Dictionary<Vector3Int, MeshData> GenerateMesh(IVoxelDataProvider voxelDataProvider)
    {
        var result = new Dictionary<Vector3Int, MeshData>();
        var threadGroups = new Vector3Int(
            Mathf.CeilToInt(_samplesPerAxis.x / 8f),
            Mathf.CeilToInt(_samplesPerAxis.y / 8f),
            Mathf.CeilToInt(_samplesPerAxis.z / 8f)
        );

        for (int x = 0; x < _config.WorldSize.x; x++)
        for (int y = 0; y < _config.WorldSize.y; y++)
        for (int z = 0; z < _config.WorldSize.z; z++)
        {
            var coord = new Vector3Int(x, y, z);

            float[] voxels = voxelDataProvider.Generate(coord);
            _voxelBuffer.SetData(voxels);

            _triangleBuffer.SetCounterValue(0);

            Vector3 offset = Vector3.Scale(coord, _config.ChunkSize);
            _computeShader.SetFloats("ChunkOffset", offset.x, offset.y, offset.z);

            _computeShader.Dispatch(_kernel, threadGroups.x, threadGroups.y, threadGroups.z);

            ComputeBuffer.CopyCount(_triangleBuffer, _countBuffer, 0);
            int[] countData = { 0 };
            _countBuffer.GetData(countData);
            int triCount = countData[0];

            if (triCount == 0)
                continue;

            Triangle[] tris = new Triangle[triCount];
            _triangleBuffer.GetData(tris, 0, 0, triCount);

            result[coord] = BuildMeshData(tris);
        }

        return result;
    }

    private MeshData BuildMeshData(Triangle[] tris)
    {
        var vertices = new Vector3[tris.Length * 3];
        var triangles = new int[tris.Length * 3];

        for (int i = 0; i < tris.Length; i++)
        {
            int idx = i * 3;

            vertices[idx]     = tris[i].VertexA;
            vertices[idx + 1] = tris[i].VertexB;
            vertices[idx + 2] = tris[i].VertexC;

            triangles[idx]     = idx;
            triangles[idx + 1] = idx + 1;
            triangles[idx + 2] = idx + 2;
        }

        return new MeshData(vertices, triangles);
    }
    
    public void Dispose()
    {
        _voxelBuffer?.Release();
        _triangleBuffer?.Release();
        _countBuffer?.Release();
    }
}
