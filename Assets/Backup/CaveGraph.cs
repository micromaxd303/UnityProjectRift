using System.Collections.Generic;
using UnityEngine;


public enum NodeRole
{
    Entrance,
    Objective,
    SideMission,
    Loot,
    Exit,
    Empty
}

[System.Serializable]
public struct GraphNode
{
    public Vector3 Position;
    public NodeRole Role;
    public bool IsAnchor;

    public GraphNode(Vector3 position, NodeRole role, bool isAnchor)
    {
        Position = position;
        Role = role;
        IsAnchor = isAnchor;
    }
}
