using CaveGraphBuilder;
using UnityEngine;

/// <summary>
/// Собирает LevelContext (этап 8) из результатов пещерного генератора: граф, SDF-сцена, проверка.
/// Позиции остаются в локальных координатах пещеры (root), чтобы двигаться вместе с ней.
/// </summary>
public static class LevelContextBuilder
{
    public static LevelContext Build(LevelRequest request, LevelInfo.LevelRequirements requirements, CaveGraph graph,
        int seed, SdfScene scene, LevelValidationResult validation, Transform root)
    {
        var ctx = new LevelContext
        {
            Request = request,
            Seed = seed,
            Validated = validation != null && validation.Success,
            Root = root,
            StartRegion = graph.Start,
            EstimatedDurationSeconds = graph.EstimatedDuration,
            TargetDurationSeconds = requirements.TargetDurationSeconds,
            MinRiftPointsPerRegion = requirements.MinRiftPointsPerRegion
        };

        for (int i = 0; i < graph.Nodes.Count; i++)
        {
            var n = graph.Nodes[i];
            ctx.Regions.Add(new LevelRegion
            {
                Index = i,
                Role = n.Role,
                SideMissionId = n.SideMissionId ?? "",
                Center = n.Position,
                Radius = n.Radius,
                Distance01 = n.Distance01
            });
        }

        for (int ei = 0; ei < graph.Edges.Count; ei++)
        {
            var e = graph.Edges[ei];
            var c = new LevelConnection
            {
                A = e.A,
                B = e.B,
                IsLoop = e.IsLoop,
                Type = e.Type switch
                {
                    EdgeType.Drop => LevelConnectionType.Drop,
                    EdgeType.Crawl => LevelConnectionType.Crawl,
                    _ => LevelConnectionType.Walk
                }
            };
            if (c.Type == LevelConnectionType.Drop)
                c.From = graph.Nodes[e.A].Position.y >= graph.Nodes[e.B].Position.y ? e.A : e.B;

            ctx.Connections.Add(c);
            ctx.Regions[e.A].Connections.Add(ei);
            ctx.Regions[e.B].Connections.Add(ei);
        }

        if (scene != null)
            foreach (var a in scene.Anchors)
                ctx.Anchors.Add(new LevelAnchor
                {
                    Kind = a.Kind switch
                    {
                        SdfAnchorKind.Incubator => LevelAnchorKind.Incubator,
                        SdfAnchorKind.Pad => LevelAnchorKind.Pad,
                        SdfAnchorKind.Cover => LevelAnchorKind.Cover,
                        _ => LevelAnchorKind.Pillar
                    },
                    Position = a.Position,
                    Forward = a.Forward,
                    Region = a.Node
                });

        ctx.PlayerSpawn = FindSpawn(graph, validation);

        AddObjectives(ctx, request);
        return ctx;
    }

    /// <summary>Спавн — точка пола в стартовой комнате из проверки (этап 4), иначе центр стартовой комнаты.</summary>
    private static Vector3 FindSpawn(CaveGraph graph, LevelValidationResult validation)
    {
        var a = validation?.Analysis;
        if (a != null && a.SpawnIndex >= 0)
            return validation.Field.ToLocal(a.SpawnIndex);
        return graph.Start >= 0 ? graph.Nodes[graph.Start].Position : Vector3.zero;
    }

    /// <summary>
    /// Основная цель — регионы с ролями основной миссии (без старта), дополнительные — регионы со своим Id.
    /// К цели привязываются ниши и пятачки в её регионах.
    /// </summary>
    private static void AddObjectives(LevelContext ctx, LevelRequest request)
    {
        var main = new LevelObjective { IsMain = true, Mission = request.MainMission };
        foreach (var r in ctx.Regions)
            if (string.IsNullOrEmpty(r.SideMissionId) &&
                r.Role != LevelInfo.RegionRole.None && r.Role != LevelInfo.RegionRole.Start)
                main.Regions.Add(r.Index);
        AttachAnchors(ctx, main);
        ctx.Objectives.Add(main);

        for (int i = 0; i < request.SideMissions.Count; i++)
        {
            string id = request.SideMissionId(i);
            var side = new LevelObjective
            {
                IsMain = false,
                Mission = request.MainMission,
                SideMissionId = id,
                SideKind = request.SideMissions[i].Kind
            };
            foreach (var r in ctx.Regions)
                if (r.SideMissionId == id)
                    side.Regions.Add(r.Index);
            // Вынести предмет: точка сдачи — старт (решение по контракту)
            if (side.SideKind == LevelInfo.SideMissionKind.CarryOut && !side.Regions.Contains(ctx.StartRegion))
                side.Regions.Add(ctx.StartRegion);
            AttachAnchors(ctx, side);
            ctx.Objectives.Add(side);
        }
    }

    private static void AttachAnchors(LevelContext ctx, LevelObjective objective)
    {
        for (int i = 0; i < ctx.Anchors.Count; i++)
        {
            var a = ctx.Anchors[i];
            if ((a.Kind == LevelAnchorKind.Incubator || a.Kind == LevelAnchorKind.Pad) && objective.Regions.Contains(a.Region))
                objective.Anchors.Add(i);
        }
    }
}
