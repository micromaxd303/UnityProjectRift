
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public static class MeshOptimizer
{
    private const float WELD_THRESHOLD = 0.0001f;

    public static MeshData WeldVertices(MeshData input)
    {
        var vertexMap = new Dictionary<long, List<int>>();
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var triangles = new int[input.Triangles.Length];

        for (int i = 0; i < input.Vertices.Length; i++)
        {
            Vector3 v = input.Vertices[i];
            long hash = HashVertex(v);
            int foundIndex = -1;

            if (vertexMap.TryGetValue(hash, out var candidates))
            {
                foreach (int c in candidates)
                {
                    if (Vector3.Distance(vertices[c], v) < WELD_THRESHOLD)
                    {
                        foundIndex = c;
                        break;
                    }
                }
            }

            if (foundIndex >= 0)
            {
                normals[foundIndex] = (normals[foundIndex] + input.Normals[i]).normalized;
                triangles[i] = foundIndex;
            }
            else
            {
                int newIndex = vertices.Count;
                vertices.Add(v);
                normals.Add(input.Normals[i]);
                triangles[i] = newIndex;

                if (!vertexMap.ContainsKey(hash))
                    vertexMap[hash] = new List<int>();
                vertexMap[hash].Add(newIndex);
            }
        }

        return new MeshData(
            vertices.ToArray(),
            normals.ToArray(),
            triangles.ToArray()
        );
    }

    private static long HashVertex(Vector3 v)
    {
        long x = (long)(v.x / WELD_THRESHOLD);
        long y = (long)(v.y / WELD_THRESHOLD);
        long z = (long)(v.z / WELD_THRESHOLD);
        return (x * 73856093L) ^ (y * 19349663L) ^ (z * 83492791L);
    }
}