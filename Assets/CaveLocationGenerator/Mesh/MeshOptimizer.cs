using System.Collections.Generic;
using UnityEngine;

public static class MeshOptimizer
{
    public static MeshData WeldVertices(MeshData input, float threshold)
    {
        int count = input.Vertices.Length;
        var cells = new Dictionary<Vector3Int, List<int>>(count);
        var vertices = new List<Vector3>(count / 3);
        var remap = new int[count]; // старый индекс вершины -> новый

        float inv = 1f / threshold;
        float sqrThreshold = threshold * threshold;

        for (int i = 0; i < count; i++)
        {
            Vector3 v = input.Vertices[i];
            var cell = new Vector3Int(
                Mathf.FloorToInt(v.x * inv),
                Mathf.FloorToInt(v.y * inv),
                Mathf.FloorToInt(v.z * inv));

            int found = FindNear(cells, vertices, cell, v, sqrThreshold);

            if (found < 0)
            {
                found = vertices.Count;
                vertices.Add(v);

                if (!cells.TryGetValue(cell, out var list))
                    cells[cell] = list = new List<int>(1);
                list.Add(found);
            }

            remap[i] = found;
        }

        // Треугольники переносим через input.Triangles, а не по индексу вершины,
        // и выкидываем вырожденные.
        var src = input.Triangles;
        var triangles = new List<int>(src.Length);
        for (int t = 0; t < src.Length; t += 3)
        {
            int a = remap[src[t]];
            int b = remap[src[t + 1]];
            int c = remap[src[t + 2]];

            if (a == b || b == c || a == c)
                continue;

            triangles.Add(a);
            triangles.Add(b);
            triangles.Add(c);
        }

        return new MeshData(vertices.ToArray(), triangles.ToArray());
    }

    // Проверяем 27 ячеек: близкие вершины могут лежать по разные стороны границы ячейки.
    private static int FindNear(Dictionary<Vector3Int, List<int>> cells, List<Vector3> vertices,
                                Vector3Int cell, Vector3 v, float sqrThreshold)
    {
        for (int dx = -1; dx <= 1; dx++)
        for (int dy = -1; dy <= 1; dy++)
        for (int dz = -1; dz <= 1; dz++)
        {
            if (!cells.TryGetValue(cell + new Vector3Int(dx, dy, dz), out var list))
                continue;

            foreach (int idx in list)
                if ((vertices[idx] - v).sqrMagnitude < sqrThreshold)
                    return idx;
        }
        return -1;
    }

    public static MeshData LaplacianSmooth(MeshData input, int iterations, float strength)
    {
        // TODO: реализовать другой метод сглаживания.
        // Пока возвращает меш без изменений, чтобы проект компилировался.
        return input;
    }
}