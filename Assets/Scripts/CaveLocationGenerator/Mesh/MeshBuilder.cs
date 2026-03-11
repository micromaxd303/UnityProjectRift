using System.Collections.Generic;
using UnityEngine;

public class MeshBuilder
{
    private readonly Material _material;
    private readonly Transform _parent;

    public MeshBuilder(Material material, Transform parent)
    {
        _material = material;
        _parent = parent;
    }

    public Dictionary<Vector3Int, GameObject> Build(Dictionary<Vector3Int, MeshData> chunks)
    {
        var result = new Dictionary<Vector3Int, GameObject>();

        foreach (var (coord, data) in chunks)
        {
            if (data.Vertices.Length == 0)
                continue;

            var optimized = MeshOptimizer.WeldVertices(data);

            if (optimized.Vertices.Length < 3)
                continue;

            var mesh = new Mesh
            {
                vertices = optimized.Vertices,
                triangles = optimized.Triangles
            };
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();

            var go = new GameObject($"Chunk_{coord.x}_{coord.y}_{coord.z}");
            go.layer = LayerMask.NameToLayer("CaveChunk");
            go.transform.SetParent(_parent);

            go.AddComponent<MeshFilter>().mesh = mesh;
            go.AddComponent<MeshRenderer>().material = _material;

            if (optimized.Triangles.Length >= 3)
                go.AddComponent<MeshCollider>().sharedMesh = mesh;

            result[coord] = go;
        }

        return result;
    }
}
