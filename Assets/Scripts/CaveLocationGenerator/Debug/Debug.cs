using UnityEngine;

public static class VoxelDebug
{
    /// <summary>
    /// Выводит горизонтальный срез (по Y) в консоль.
    /// '█' — solid, '·' — empty, 'x' — border (принудительно пустой)
    /// </summary>
    public static void LogSlice(float[] data, Vector3Int samples, int y, float surfaceLevel = 0f)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"--- Slice Y={y} ---");

        for (int z = samples.z - 1; z >= 0; z--)
        {
            for (int x = 0; x < samples.x; x++)
            {
                float val = data[x + y * samples.x + z * samples.x * samples.y];
                
                if (val <= -1f)
                    sb.Append("x ");      // border
                else if (val > surfaceLevel)
                    sb.Append("█ ");      // solid
                else
                    sb.Append("· ");      // empty
            }
            sb.AppendLine();
        }

        Debug.Log(sb.ToString());
    }

    /// <summary>
    /// Выводит все срезы чанка.
    /// </summary>
    public static void LogAllSlices(float[] data, Vector3Int samples, float surfaceLevel = 0f)
    {
        for (int y = 0; y < samples.y; y++)
            LogSlice(data, samples, y, surfaceLevel);
    }

    /// <summary>
    /// Рисует воксели как Gizmos в сцене.
    /// Красный — solid, синий — empty, жёлтый — около поверхности.
    /// </summary>
    public static void DrawGizmos(
        float[] data, 
        Vector3Int chunkCoord, 
        Vector3Int chunkSize, 
        float surfaceLevel = 0f, 
        float gizmoSize = 0.2f)
    {
        Vector3Int samples = chunkSize + Vector3Int.one;
        Vector3 origin = Vector3.Scale(chunkCoord, chunkSize);

        for (int z = 0; z < samples.z; z++)
        for (int y = 0; y < samples.y; y++)
        for (int x = 0; x < samples.x; x++)
        {
            float val = data[x + y * samples.x + z * samples.x * samples.y];
            Vector3 pos = origin + new Vector3(x, y, z);

            float diff = val - surfaceLevel;

            if (Mathf.Abs(diff) < 0.1f)
                Gizmos.color = Color.yellow;    // около поверхности
            else if (diff > 0)
                Gizmos.color = Color.red;       // solid
            else
                Gizmos.color = new Color(0, 0, 1, 0.1f); // empty, полупрозрачный

            Gizmos.DrawCube(pos, Vector3.one * gizmoSize);
        }
    }
    
    
    public static void LogMeshData(MeshData data, Vector3Int coord, int maxTriangles = 20)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"=== Chunk {coord} ===");
        sb.AppendLine($"Vertices: {data.Vertices.Length}, Triangles: {data.Triangles.Length / 3}");

        int badVerts = 0;
        int degenerateTris = 0;

        for (int i = 0; i < data.Vertices.Length; i++)
        {
            Vector3 v = data.Vertices[i];
            if (float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) ||
                float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z))
            {
                badVerts++;
            }
        }

        for (int i = 0; i < data.Triangles.Length; i += 3)
        {
            Vector3 a = data.Vertices[data.Triangles[i]];
            Vector3 b = data.Vertices[data.Triangles[i + 1]];
            Vector3 c = data.Vertices[data.Triangles[i + 2]];

            float maxEdge = Mathf.Max(
                Vector3.Distance(a, b),
                Vector3.Distance(b, c),
                Vector3.Distance(a, c)
            );

            // Треугольник с ребром длиннее 5 единиц — подозрительный
            if (maxEdge > 5f || float.IsNaN(maxEdge))
            {
                degenerateTris++;
                if (degenerateTris <= maxTriangles)
                {
                    sb.AppendLine($"  BAD tri [{i/3}]: A={a} B={b} C={c} maxEdge={maxEdge:F2}");
                }
            }
        }

        sb.AppendLine($"Bad vertices: {badVerts}, Degenerate triangles: {degenerateTris}");
        Debug.Log(sb.ToString());
    }
    
    public static void DumpAllTriangles(MeshData data, Vector3Int coord)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Chunk {coord} | Triangles: {data.Triangles.Length / 3}");
        sb.AppendLine("idx | VertA | VertB | VertC | maxEdge");
        sb.AppendLine("----+-------+-------+-------+--------");

        for (int i = 0; i < data.Triangles.Length; i += 3)
        {
            Vector3 a = data.Vertices[data.Triangles[i]];
            Vector3 b = data.Vertices[data.Triangles[i + 1]];
            Vector3 c = data.Vertices[data.Triangles[i + 2]];

            float maxEdge = Mathf.Max(
                Vector3.Distance(a, b),
                Vector3.Distance(b, c),
                Vector3.Distance(a, c)
            );

            sb.AppendLine($"{i/3,4} | ({a.x:F2},{a.y:F2},{a.z:F2}) | ({b.x:F2},{b.y:F2},{b.z:F2}) | ({c.x:F2},{c.y:F2},{c.z:F2}) | {maxEdge:F2}");
        }

        string path = System.IO.Path.Combine(Application.dataPath, $"chunk_{coord.x}_{coord.y}_{coord.z}_dump.txt");
        System.IO.File.WriteAllText(path, sb.ToString());
        Debug.Log($"Dump saved: {path}");
    }
}
