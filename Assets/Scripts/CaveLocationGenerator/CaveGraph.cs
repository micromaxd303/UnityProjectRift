using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Тип узла в графе пещеры.
/// </summary>
public enum CaveNodeType
{
    Tunnel,
    Hall,
    Deadend,
    Spawn,
    Extract,
    Objective
}

/// <summary>
/// Узел графа пещеры.
/// </summary>
public class CaveNode
{
    public Vector3 Position;
    public CaveNodeType Type;
    public float Radius;
    public int Degree;

    public Vector2 Position2D => new Vector2(Position.x, Position.z);
}

/// <summary>
/// Ребро графа пещеры.
/// Если HasMidpoint = true, ребро состоит из двух сегментов:
///   NodeA → Midpoint → NodeB (кривой тоннель).
/// </summary>
public class CaveEdge
{
    public int NodeA;
    public int NodeB;
    public float Radius;
    public bool IsMST;

    public bool HasMidpoint;
    public Vector3 Midpoint;
}

/// <summary>
/// Полный граф пещеры.
/// </summary>
public class CaveGraph
{
    public List<CaveNode> Nodes = new List<CaveNode>();
    public List<CaveEdge> Edges = new List<CaveEdge>();

    public int SpawnIndex = -1;
    public int ExtractIndex = -1;
    public List<int> ObjectiveIndices = new List<int>();

    public int Seed;

    public List<CaveEdge> GetEdgesFor(int nodeIndex)
    {
        var result = new List<CaveEdge>();
        foreach (var edge in Edges)
            if (edge.NodeA == nodeIndex || edge.NodeB == nodeIndex)
                result.Add(edge);
        return result;
    }

    public List<int> GetNeighbors(int nodeIndex)
    {
        var result = new List<int>();
        foreach (var edge in Edges)
        {
            if (edge.NodeA == nodeIndex) result.Add(edge.NodeB);
            else if (edge.NodeB == nodeIndex) result.Add(edge.NodeA);
        }
        return result;
    }
}