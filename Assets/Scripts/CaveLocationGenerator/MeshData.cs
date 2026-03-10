using System.Collections.Generic;
using UnityEngine;

public readonly struct MeshData
{
    public readonly Vector3[] Vertices;
    public readonly int[] Triangles;

    public MeshData(Vector3[] vertices, int[] triangles)
    {
        Vertices = vertices;
        Triangles = triangles;
    }
}