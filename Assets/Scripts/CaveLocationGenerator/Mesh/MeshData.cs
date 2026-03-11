using UnityEngine;

public readonly struct MeshData
{
    public readonly Vector3[] Vertices;
    public readonly Vector3[] Normals;
    public readonly int[] Triangles;

    public MeshData(Vector3[] vertices, Vector3[] normals, int[] triangles)
    {
        Vertices = vertices;
        Normals = normals;
        Triangles = triangles;
    }
}