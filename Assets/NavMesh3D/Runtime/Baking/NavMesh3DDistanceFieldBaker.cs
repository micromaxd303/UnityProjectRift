using System;
using System.Threading.Tasks;
using Unity.Mathematics;
using UnityEngine;

namespace NavMesh3DSystem
{
    /// <summary>
    /// Источник сетки вместо физики. NavMesh3D.Bake() вызывает TryBake, если источник назначен в BakeSource.
    /// </summary>
    public interface INavMesh3DBakeSource
    {
        /// <returns>false — данных нет, NavMesh3D запечёт через физику</returns>
        bool TryBake(NavMesh3D navMesh);
    }
    
    /// <summary>
    /// Поле расстояний до препятствий на регулярной сетке сэмплов.
    /// Координаты — в локальном пространстве объекта, которое передаётся в BakeFromDistanceField.
    /// </summary>
    public struct NavMesh3DDistanceField
    {
        /// <summary>Расстояние до ближайшего препятствия в каждом сэмпле, м. 0 — внутри препятствия.</summary>
        public float[] distance;

        /// <summary>Количество сэмплов по осям. Индекс сэмпла: x + size.x * (y + size.y * z).</summary>
        public Vector3Int size;

        /// <summary>Шаг между сэмплами, м.</summary>
        public float spacing;

        /// <summary>Локальная позиция сэмпла (0, 0, 0).</summary>
        public Vector3 origin;

        /// <summary>
        /// На сколько настоящая поверхность ближе, чем говорит расстояние в сэмпле.
        /// Для EDT по вокселям поверхность лежит между сэмплами, обычно половина шага.
        /// </summary>
        public float surfaceBias;

        public Vector3 Extent => (Vector3)(size - Vector3Int.one) * spacing;
        public Vector3 Center => origin + Extent * 0.5f;

        /// <summary>Трилинейное расстояние в локальной точке. За пределами поля — 0.</summary>
        public float Sample(float3 local)
        {
            float3 g = (local - (float3)origin) / spacing;
            int3 i = (int3)math.floor(g);

            if (i.x < 0 || i.y < 0 || i.z < 0 ||
                i.x >= size.x - 1 || i.y >= size.y - 1 || i.z >= size.z - 1)
                return 0f;

            float3 t = g - i;
            int sx = size.x, sxy = size.x * size.y;
            int i000 = i.x + sx * i.y + sxy * i.z;

            float c00 = math.lerp(distance[i000], distance[i000 + 1], t.x);
            float c10 = math.lerp(distance[i000 + sx], distance[i000 + sx + 1], t.x);
            float c01 = math.lerp(distance[i000 + sxy], distance[i000 + sxy + 1], t.x);
            float c11 = math.lerp(distance[i000 + sxy + sx], distance[i000 + sxy + sx + 1], t.x);

            return math.lerp(math.lerp(c00, c10, t.y), math.lerp(c01, c11, t.y), t.z);
        }
    }

    /// <summary>
    /// Запекание из готового поля расстояний (например EDT воксельной пещеры).
    ///
    /// ПОЧЕМУ НЕ ФИЗИКА:
    /// OverlapSphere по MeshCollider пещеры видит только тонкую оболочку из треугольников.
    /// Всё внутри толщи камня получается «проходимым»: лишние узлы, ложная связность,
    /// GetNearestWalkable может вернуть точку в скале. Поле расстояний знает, где камень,
    /// а одно чтение массива на узел быстрее любого физического запроса.
    /// </summary>
    public static class NavMesh3DDistanceFieldBaker
    {
        public static NavMesh3DBaker.BakeResult Bake(
            NavMesh3DChunk[,,] chunks,
            int3 chunkCount,
            float cellSize,
            float agentRadius,
            float3 gridCornerLocal,
            NavMesh3DDistanceField field)
        {
            var startTime = DateTime.Now;
            var result = new NavMesh3DBaker.BakeResult();

            int size = NavMesh3DChunk.Size;
            int total = chunkCount.x * chunkCount.y * chunkCount.z;
            float required = agentRadius + field.surfaceBias;
            int walkable = 0;

            // Каждый чанк пишет только свою маску — можно параллельно
            Parallel.For(0, total, () => 0, (c, _, local) =>
            {
                int cy = c / (chunkCount.x * chunkCount.z);
                int cx = (c % (chunkCount.x * chunkCount.z)) / chunkCount.z;
                int cz = c % chunkCount.z;
                var chunk = chunks[cx, cy, cz];
                float3 chunkCorner = gridCornerLocal + new float3(cx, cy, cz) * (size * cellSize);

                for (int n = 0; n < NavMesh3DChunk.NodeCount; n++)
                {
                    int y = n >> 8, x = (n >> 4) & 15, z = n & 15;
                    float3 p = chunkCorner + (new float3(x, y, z) + 0.5f) * cellSize;
                    bool ok = field.Sample(p) >= required;
                    chunk.SetWalkable(n, ok);
                    if (ok) local++;
                }

                return local;
            }, local => System.Threading.Interlocked.Add(ref walkable, local));

            result.success = true;
            result.totalNodes = total * NavMesh3DChunk.NodeCount;
            result.walkableNodes = walkable;
            result.blockedNodes = result.totalNodes - walkable;
            result.timeSeconds = (float)(DateTime.Now - startTime).TotalSeconds;
            return result;
        }
    }
}
