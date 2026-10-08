using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Отдаёт бэкендам MC уже проверенное поле (этап 5 после этапа 4).
/// Чанки без воздуха пропускаются.
/// </summary>
public class VoxelFieldProvider : IVoxelDataProvider, ISparseVoxelDataProvider
{
    private readonly VoxelField _field;
    private readonly HashSet<Vector3Int> _chunksWithAir = new();

    public VoxelField Field => _field;

    public VoxelFieldProvider(VoxelField field)
    {
        _field = field;
        for (int x = 0; x < field.WorldChunks.x; x++)
        for (int y = 0; y < field.WorldChunks.y; y++)
        for (int z = 0; z < field.WorldChunks.z; z++)
        {
            var coord = new Vector3Int(x, y, z);
            if (field.ChunkHasAir(coord))
                _chunksWithAir.Add(coord);
        }
    }

    public bool IsSolidChunk(Vector3Int chunkCoord) => !_chunksWithAir.Contains(chunkCoord);

    public float[] Generate(Vector3Int chunkCoord) => _field.ReadChunk(chunkCoord);
}
