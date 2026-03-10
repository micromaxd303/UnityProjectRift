using UnityEngine;

public interface IVoxelDataProvider
{
    /// <summary>
    /// Генерирует скалярное поле плотности для чанка.
    /// </summary>
    /// <param name="chunkCoord">Координата чанка в сетке чанков (не в мировых единицах)</param>
    /// <param name="samplesPerAxis">Количество сэмплов по каждой оси (обычно ChunkSize + 1)</param>
    /// <returns>Плоский массив float[x + y * sx + z * sx * sy], где sx/sy = samplesPerAxis</returns>
    float[] Generate(Vector3Int chunkCoord, Vector3Int samplesPerAxis);
}