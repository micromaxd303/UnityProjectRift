using System;
using System.Collections.Generic;
using UnityEngine;

public interface IMarchingCubesBackend : IDisposable
{
    void Initialize(MarchingCubesConfig config);
    Dictionary<Vector3Int, MeshData> GenerateMesh(IVoxelDataProvider voxelDataProvider);
}
