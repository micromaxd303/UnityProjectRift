using System.Collections.Generic;
using UnityEngine;

public class MeshBuilder
{
    private readonly Material _material;
    private readonly Transform _parent;
    private readonly float _uvScale;

    public MeshBuilder(Material material, Transform parent, float uvScale = 1f)
    {
        _material = material;
        _parent = parent;
        _uvScale = uvScale;
    }

    public Dictionary<Vector3Int, GameObject> Build(Dictionary<Vector3Int, MeshData> chunks)
    {
        var result = new Dictionary<Vector3Int, GameObject>();

        foreach (var (coord, data) in chunks)
        {
            if (data.Vertices.Length == 0)
                continue;

            var optimized = MeshOptimizer.WeldVertices(data);

            var mesh = new Mesh
            {
                vertices = optimized.Vertices,
                normals = optimized.Normals,
                triangles = optimized.Triangles
            };
            mesh.RecalculateTangents();

            var go = new GameObject($"Chunk_{coord.x}_{coord.y}_{coord.z}");
            go.transform.SetParent(_parent);

            go.AddComponent<MeshFilter>().mesh = mesh;
            go.AddComponent<MeshRenderer>().material = _material;
            go.AddComponent<MeshCollider>().sharedMesh = mesh;

            result[coord] = go;
        }

        return result;
    }

    private Vector2[] GenerateUVs(Vector3[] vertices, Vector3[] normals)
    {
        var uvs = new Vector2[vertices.Length];

        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 v = vertices[i] * _uvScale;
            Vector3 n = normals[i];

            float absX = Mathf.Abs(n.x);
            float absY = Mathf.Abs(n.y);
            float absZ = Mathf.Abs(n.z);

            // Проецируем по доминирующей оси нормали
            if (absY >= absX && absY >= absZ)
                uvs[i] = new Vector2(v.x, v.z); // пол/потолок
            else if (absX >= absZ)
                uvs[i] = new Vector2(v.z, v.y); // стена лево/право
            else
                uvs[i] = new Vector2(v.x, v.y); // стена перед/зад
        }

        return uvs;
    }
}
