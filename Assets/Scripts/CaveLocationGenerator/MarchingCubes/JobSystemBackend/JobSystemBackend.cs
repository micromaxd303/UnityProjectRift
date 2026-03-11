using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

public class JobSystemBackend : IMarchingCubesBackend
{
    private MarchingCubesConfig _config;
    private int3 _samplesPerAxis;

    private NativeArray<int> _triTable;
    private NativeArray<int3> _vertexOffset;
    private NativeArray<int2> _edgeConnection;

    public void Initialize(MarchingCubesConfig config)
    {
        _config = config;
        _samplesPerAxis = new int3(config.ChunkSize.x + 1, config.ChunkSize.y + 1, config.ChunkSize.z + 1);

        _triTable = MarchingCubesTables.GetNativeTriTable();
        _vertexOffset = MarchingCubesTables.GetNativeVertexOffset();
        _edgeConnection = MarchingCubesTables.GetNativeEdgeConnection();
    }

    public Dictionary<Vector3Int, MeshData> GenerateMesh(IVoxelDataProvider voxelDataProvider)
    {
        var allCoords = new List<Vector3Int>();
        for (int x = 0; x < _config.WorldSize.x; x++)
        for (int y = 0; y < _config.WorldSize.y; y++)
        for (int z = 0; z < _config.WorldSize.z; z++)
            allCoords.Add(new Vector3Int(x, y, z));

        int chunkCount = allCoords.Count;

        // Подготовка воксельных данных
        var voxelArrays = new NativeArray<float>[chunkCount];
        for (int i = 0; i < chunkCount; i++)
        {
            float[] voxels = voxelDataProvider.Generate(allCoords[i]);
            voxelArrays[i] = new NativeArray<float>(voxels, Allocator.TempJob);
        }

        // === Проход 1: подсчёт треугольников ===
        var triCounts = new NativeArray<int>[chunkCount];
        var countHandles = new NativeArray<JobHandle>(chunkCount, Allocator.Temp);

        for (int i = 0; i < chunkCount; i++)
        {
            triCounts[i] = new NativeArray<int>(1, Allocator.TempJob);

            var job = new CountTrianglesJob
            {
                Voxels = voxelArrays[i],
                TriangleTable = _triTable,
                SamplesPerAxis = _samplesPerAxis,
                IsoLevel = _config.SurfaceLevel,
                TriCount = triCounts[i]
            };

            countHandles[i] = job.Schedule();
        }

        JobHandle.CompleteAll(countHandles);
        countHandles.Dispose();

        // === Проход 2: генерация треугольников ===
        var triArrays = new NativeArray<Triangle>[chunkCount];
        var genHandles = new NativeArray<JobHandle>(chunkCount, Allocator.Temp);
        
        Vector3 worldSize = Vector3.Scale(
            new Vector3(_config.WorldSize.x, _config.WorldSize.y, _config.WorldSize.z),
            new Vector3(_config.ChunkSize.x, _config.ChunkSize.y, _config.ChunkSize.z));
        float3 centerOffset = -(float3)(worldSize * 0.5f);

        for (int i = 0; i < chunkCount; i++)
        {
            int count = triCounts[i][0];
            triArrays[i] = new NativeArray<Triangle>(
                math.max(count, 1), Allocator.TempJob);

            if (count == 0)
            {
                genHandles[i] = default;
                continue;
            }

            Vector3Int coord = allCoords[i];
            var job = new GenerateTrianglesJob
            {
                Voxels = voxelArrays[i],
                TriangleTable = _triTable,
                VertexOffset = _vertexOffset,
                EdgeConnection = _edgeConnection,
                SamplesPerAxis = _samplesPerAxis,
                ChunkOffset = new float3(
                    coord.x * _config.ChunkSize.x,
                    coord.y * _config.ChunkSize.y,
                    coord.z * _config.ChunkSize.z) + centerOffset,
                IsoLevel = _config.SurfaceLevel,
                Output = triArrays[i]
            };

            genHandles[i] = job.Schedule();
        }

        JobHandle.CompleteAll(genHandles);
        genHandles.Dispose();

        // === Сборка результатов ===
        var result = new Dictionary<Vector3Int, MeshData>();

        for (int i = 0; i < chunkCount; i++)
        {
            int count = triCounts[i][0];

            if (count > 0)
                result[allCoords[i]] = BuildMeshData(triArrays[i], count);

            triCounts[i].Dispose();
            triArrays[i].Dispose();
            voxelArrays[i].Dispose();
        }

        return result;
    }

    private MeshData BuildMeshData(NativeArray<Triangle> tris, int count)
    {
        var vertices = new Vector3[count * 3];
        var normals = new Vector3[count * 3];
        var triangles = new int[count * 3];

        for (int i = 0; i < count; i++)
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
        MarchingCubesTables.DisposeAll();
    }
}