using UnityEngine;

public class VoxelDataGenerator : IVoxelDataProvider
{
    private readonly Vector3 _worldCenter;
    private readonly Vector3 _worldMax;
    private readonly float _radius;
    private readonly Vector3Int _chunkSize;

    public VoxelDataGenerator(MarchingCubesConfig config)
    {
        _chunkSize = config.ChunkSize;
        _worldMax = Vector3.Scale(config.WorldSize, _chunkSize);
        _worldCenter = _worldMax * 0.5f;
        _radius = Mathf.Min(_worldMax.x, _worldMax.y, _worldMax.z) * 0.4f;
    }

    public float[] Generate(Vector3Int chunkCoord)
    {
        Vector3Int samples = _chunkSize + Vector3Int.one;
        int sx = samples.x;
        int sy = samples.y;
        int sz = samples.z;
        
        var data = new float[sx * sy * sz];

        Vector3 origin = Vector3.Scale(chunkCoord, _chunkSize);

        for (int z = 0; z < sz; z++)
        for (int y = 0; y < sy; y++)
        for (int x = 0; x < sx; x++)
        {
            Vector3 worldPos = origin + new Vector3(x, y, z);

            float density;
            if (IsBorder(worldPos))
            {
                density = -100f;
            }
            else
            {
                float dist = Vector3.Distance(worldPos, _worldCenter);
                density = _radius - dist;
            }

            data[x + y * sx + z * sx * sy] = density;
        }

        return data;
    }

    private bool IsBorder(Vector3 worldPos)
    {
        return worldPos.x <= 0 || worldPos.x >= _worldMax.x
                               || worldPos.y <= 0 || worldPos.y >= _worldMax.y
                               || worldPos.z <= 0 || worldPos.z >= _worldMax.z;
    }
}
