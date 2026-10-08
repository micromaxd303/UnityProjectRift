using UnityEngine;

/// <summary>
/// Поле вокселей всего мира (этап 3): плотность и метка региона в каждом сэмпле.
/// Плотность > 0 — камень, < 0 — воздух. Сэмплов по оси на один больше, чем вокселей.
/// Живёт от вокселизации до меширования, чтобы его можно было проверить и починить.
/// </summary>
public class VoxelField
{
    public const float SolidDensity = 100f;

    public readonly Vector3Int Size;        // сэмплов по осям
    public readonly Vector3Int ChunkSize;
    public readonly Vector3Int WorldChunks;
    public readonly float VoxelSize;
    public readonly float IsoLevel;
    public readonly Vector3 HalfWorld;

    public readonly float[] Density;
    public readonly ushort[] Labels;

    public int Count => Density.Length;

    public VoxelField(MarchingCubesConfig config)
    {
        ChunkSize = config.ChunkSize;
        WorldChunks = config.WorldSize;
        VoxelSize = config.VoxelSize;
        IsoLevel = config.SurfaceLevel;
        HalfWorld = config.TotalWorldSize * 0.5f;
        Size = Vector3Int.Scale(WorldChunks, ChunkSize) + Vector3Int.one;

        Density = new float[Size.x * Size.y * Size.z];
        Labels = new ushort[Density.Length];
        for (int i = 0; i < Density.Length; i++) Density[i] = SolidDensity;
    }

    public int Index(int x, int y, int z) => x + Size.x * (y + Size.y * z);

    public Vector3Int Coord(int index)
    {
        int x = index % Size.x;
        int yz = index / Size.x;
        return new Vector3Int(x, yz % Size.y, yz / Size.y);
    }

    public bool InBounds(int x, int y, int z) =>
        x >= 0 && y >= 0 && z >= 0 && x < Size.x && y < Size.y && z < Size.z;

    public bool IsAir(int index) => Density[index] < IsoLevel;

    public bool IsAir(int x, int y, int z) => InBounds(x, y, z) && Density[Index(x, y, z)] < IsoLevel;

    public Vector3 ToLocal(int x, int y, int z) => new Vector3(x, y, z) * VoxelSize - HalfWorld;

    public Vector3 ToLocal(int index)
    {
        var c = Coord(index);
        return ToLocal(c.x, c.y, c.z);
    }

    public Vector3Int FromLocal(Vector3 p)
    {
        Vector3 v = (p + HalfWorld) / VoxelSize;
        return new Vector3Int(
            Mathf.Clamp(Mathf.RoundToInt(v.x), 0, Size.x - 1),
            Mathf.Clamp(Mathf.RoundToInt(v.y), 0, Size.y - 1),
            Mathf.Clamp(Mathf.RoundToInt(v.z), 0, Size.z - 1));
    }

    /// <summary>Записывает данные чанка (формат IVoxelDataProvider) в поле.</summary>
    public void WriteChunk(Vector3Int coord, float[] data, ushort[] labels)
    {
        int sx = ChunkSize.x + 1, sy = ChunkSize.y + 1, sz = ChunkSize.z + 1;
        Vector3Int o = Vector3Int.Scale(coord, ChunkSize);

        for (int z = 0; z < sz; z++)
        for (int y = 0; y < sy; y++)
        for (int x = 0; x < sx; x++)
        {
            int src = x + y * sx + z * sx * sy;
            int dst = Index(o.x + x, o.y + y, o.z + z);
            Density[dst] = data[src];
            Labels[dst] = labels != null ? labels[src] : (ushort)0;
        }
    }

    /// <summary>Вырезает данные чанка в формате IVoxelDataProvider.</summary>
    public float[] ReadChunk(Vector3Int coord)
    {
        int sx = ChunkSize.x + 1, sy = ChunkSize.y + 1, sz = ChunkSize.z + 1;
        var data = new float[sx * sy * sz];
        Vector3Int o = Vector3Int.Scale(coord, ChunkSize);

        for (int z = 0; z < sz; z++)
        for (int y = 0; y < sy; y++)
        for (int x = 0; x < sx; x++)
            data[x + y * sx + z * sx * sy] = Density[Index(o.x + x, o.y + y, o.z + z)];

        return data;
    }

    public bool ChunkHasAir(Vector3Int coord)
    {
        Vector3Int o = Vector3Int.Scale(coord, ChunkSize);
        for (int z = 0; z <= ChunkSize.z; z++)
        for (int y = 0; y <= ChunkSize.y; y++)
        for (int x = 0; x <= ChunkSize.x; x++)
            if (Density[Index(o.x + x, o.y + y, o.z + z)] < IsoLevel)
                return true;
        return false;
    }
}
