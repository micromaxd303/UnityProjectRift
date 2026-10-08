using UnityEngine;

/// <summary>
/// Провайдер, который заранее знает, какие чанки целиком состоят из камня.
/// Бэкенды Marching Cubes такие чанки пропускают.
/// </summary>
public interface ISparseVoxelDataProvider
{
    bool IsSolidChunk(Vector3Int chunkCoord);
}
