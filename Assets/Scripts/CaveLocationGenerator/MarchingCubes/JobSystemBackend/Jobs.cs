using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

[BurstCompile]
public struct CountTrianglesJob : IJob
{
    [ReadOnly] public NativeArray<float> Voxels;
    [ReadOnly] public NativeArray<int> TriangleTable;
    public int3 SamplesPerAxis;
    public float IsoLevel;

    [WriteOnly] public NativeArray<int> TriCount;

    public void Execute()
    {
        int count = 0;

        for (int z = 0; z < SamplesPerAxis.z - 1; z++)
        for (int y = 0; y < SamplesPerAxis.y - 1; y++)
        for (int x = 0; x < SamplesPerAxis.x - 1; x++)
        {
            int cubeIndex = GetCubeIndex(x, y, z);

            if (cubeIndex == 0 || cubeIndex == 255)
                continue;

            for (int i = 0; TriangleTable[cubeIndex * 16 + i] != -1; i += 3)
                count++;
        }

        TriCount[0] = count;
    }

    private int GetCubeIndex(int x, int y, int z)
    {
        int cubeIndex = 0;

        // Bourke vertex order
        if (Voxels[Index(x,     y,     z    )] < IsoLevel) cubeIndex |= 1;
        if (Voxels[Index(x + 1, y,     z    )] < IsoLevel) cubeIndex |= 2;
        if (Voxels[Index(x + 1, y + 1, z    )] < IsoLevel) cubeIndex |= 4;
        if (Voxels[Index(x,     y + 1, z    )] < IsoLevel) cubeIndex |= 8;
        if (Voxels[Index(x,     y,     z + 1)] < IsoLevel) cubeIndex |= 16;
        if (Voxels[Index(x + 1, y,     z + 1)] < IsoLevel) cubeIndex |= 32;
        if (Voxels[Index(x + 1, y + 1, z + 1)] < IsoLevel) cubeIndex |= 64;
        if (Voxels[Index(x,     y + 1, z + 1)] < IsoLevel) cubeIndex |= 128;

        return cubeIndex;
    }

    private int Index(int x, int y, int z)
    {
        return x + y * SamplesPerAxis.x + z * SamplesPerAxis.x * SamplesPerAxis.y;
    }
}

[BurstCompile]
public struct GenerateTrianglesJob : IJob
{
    [ReadOnly] public NativeArray<float> Voxels;
    [ReadOnly] public NativeArray<int> TriangleTable;
    [ReadOnly] public NativeArray<int3> VertexOffset;
    [ReadOnly] public NativeArray<int2> EdgeConnection;

    public int3 SamplesPerAxis;
    public float3 ChunkOffset;
    public float IsoLevel;

    [WriteOnly] public NativeArray<Triangle> Output;

    public void Execute()
    {
        int triIndex = 0;

        for (int z = 0; z < SamplesPerAxis.z - 1; z++)
        for (int y = 0; y < SamplesPerAxis.y - 1; y++)
        for (int x = 0; x < SamplesPerAxis.x - 1; x++)
        {
            int cubeIndex = GetCubeIndex(x, y, z);

            if (cubeIndex == 0 || cubeIndex == 255)
                continue;

            int3 id = new int3(x, y, z);
            float8 corners = GetCorners(id);

            for (int i = 0; TriangleTable[cubeIndex * 16 + i] != -1; i += 3)
            {
                int2 e0 = EdgeConnection[TriangleTable[cubeIndex * 16 + i]];
                int2 e1 = EdgeConnection[TriangleTable[cubeIndex * 16 + i + 1]];
                int2 e2 = EdgeConnection[TriangleTable[cubeIndex * 16 + i + 2]];

                Output[triIndex++] = new Triangle
                {
                    VertexA = InterpolateVertex(e0, id, corners),
                    VertexB = InterpolateVertex(e1, id, corners),
                    VertexC = InterpolateVertex(e2, id, corners)
                };
            }
        }
    }

    private float3 InterpolateVertex(int2 edge, int3 id, float8 corners)
    {
        float3 p0 = (float3)(id + VertexOffset[edge.x]) + ChunkOffset;
        float3 p1 = (float3)(id + VertexOffset[edge.y]) + ChunkOffset;

        float v0 = GetCornerValue(corners, edge.x);
        float v1 = GetCornerValue(corners, edge.y);

        float denom = v1 - v0;
        float t = math.abs(denom) > 0.00001f ? (IsoLevel - v0) / denom : 0.5f;

        return math.lerp(p0, p1, t);
    }

    private int GetCubeIndex(int x, int y, int z)
    {
        int cubeIndex = 0;
        if (Voxels[Index(x,     y,     z    )] < IsoLevel) cubeIndex |= 1;
        if (Voxels[Index(x + 1, y,     z    )] < IsoLevel) cubeIndex |= 2;
        if (Voxels[Index(x + 1, y + 1, z    )] < IsoLevel) cubeIndex |= 4;
        if (Voxels[Index(x,     y + 1, z    )] < IsoLevel) cubeIndex |= 8;
        if (Voxels[Index(x,     y,     z + 1)] < IsoLevel) cubeIndex |= 16;
        if (Voxels[Index(x + 1, y,     z + 1)] < IsoLevel) cubeIndex |= 32;
        if (Voxels[Index(x + 1, y + 1, z + 1)] < IsoLevel) cubeIndex |= 64;
        if (Voxels[Index(x,     y + 1, z + 1)] < IsoLevel) cubeIndex |= 128;
        return cubeIndex;
    }

    private float8 GetCorners(int3 id)
    {
        return new float8
        {
            c0 = Voxels[Index(id.x,     id.y,     id.z    )],
            c1 = Voxels[Index(id.x + 1, id.y,     id.z    )],
            c2 = Voxels[Index(id.x + 1, id.y + 1, id.z    )],
            c3 = Voxels[Index(id.x,     id.y + 1, id.z    )],
            c4 = Voxels[Index(id.x,     id.y,     id.z + 1)],
            c5 = Voxels[Index(id.x + 1, id.y,     id.z + 1)],
            c6 = Voxels[Index(id.x + 1, id.y + 1, id.z + 1)],
            c7 = Voxels[Index(id.x,     id.y + 1, id.z + 1)]
        };
    }

    private float GetCornerValue(float8 corners, int index)
    {
        switch (index)
        {
            case 0: return corners.c0;
            case 1: return corners.c1;
            case 2: return corners.c2;
            case 3: return corners.c3;
            case 4: return corners.c4;
            case 5: return corners.c5;
            case 6: return corners.c6;
            default: return corners.c7;
        }
    }

    private int Index(int x, int y, int z)
    {
        return x + y * SamplesPerAxis.x + z * SamplesPerAxis.x * SamplesPerAxis.y;
    }
}

public struct float8
{
    public float c0, c1, c2, c3, c4, c5, c6, c7;
}

public struct Triangle
{
    public Vector3 VertexA;
    public Vector3 VertexB;
    public Vector3 VertexC;
}