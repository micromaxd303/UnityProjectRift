using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Строит граф пещеры: Poisson Disk → Delaunay → MST + extra → роли → кривые тоннели.
/// </summary>
public static class CaveGraphBuilder
{
    public static CaveGraph Build(CaveGenerationConfig config, Vector3 worldSize, int seed)
    {
        var rng = new System.Random(seed);
        var graph = new CaveGraph { Seed = seed };

        // --- 1. Poisson Disk (XZ) ---
        float margin = config.WorldMargin;
        var points2D = PoissonDisk2D(worldSize.x, worldSize.z,
            config.MinNodeDistance, config.MaxNodes, margin, rng);

        if (points2D.Count < 4)
        {
            Debug.LogWarning($"[CaveGraphBuilder] Только {points2D.Count} точек. Нужно минимум 4.");
            return graph;
        }

        // Y с вариацией
        float baseY = worldSize.y * config.BaseHeightNormalized;
        foreach (var p in points2D)
        {
            float y = baseY + ((float)rng.NextDouble() * 2f - 1f) * config.HeightVariation;
            y = Mathf.Clamp(y, margin, worldSize.y - margin);
            graph.Nodes.Add(new CaveNode
            {
                Position = new Vector3(p.x, y, p.y),
                Type = CaveNodeType.Tunnel,
                Radius = config.TunnelRadius,
                Degree = 0
            });
        }

        // --- 2. Delaunay → уникальные рёбра ---
        var tris = Delaunay2D(points2D);
        var edgeSet = new HashSet<long>();
        var allEdges = new List<int[]>();
        foreach (var tri in tris)
        {
            for (int i = 0; i < 3; i++)
            {
                int a = Mathf.Min(tri[i], tri[(i + 1) % 3]);
                int b = Mathf.Max(tri[i], tri[(i + 1) % 3]);
                long key = (long)a * 10000 + b;
                if (edgeSet.Add(key))
                    allEdges.Add(new[] { a, b });
            }
        }

        // --- 3. MST + extra ---
        var mstEdges = Kruskal(graph.Nodes, allEdges);
        var mstSet = new HashSet<long>();
        foreach (var e in mstEdges)
        {
            int a = Mathf.Min(e[0], e[1]), b = Mathf.Max(e[0], e[1]);
            mstSet.Add((long)a * 10000 + b);
        }

        var nonMst = allEdges.Where(e =>
        {
            int a = Mathf.Min(e[0], e[1]), b = Mathf.Max(e[0], e[1]);
            return !mstSet.Contains((long)a * 10000 + b);
        }).ToList();

        Shuffle(nonMst, rng);
        int extraCount = Mathf.RoundToInt(nonMst.Count * config.ExtraEdgeRatio);
        var finalEdges = new List<int[]>(mstEdges);
        finalEdges.AddRange(nonMst.Take(extraCount));

        // --- 4. Строим CaveEdge с кривизной ---
        foreach (var e in finalEdges)
        {
            int a = Mathf.Min(e[0], e[1]), b = Mathf.Max(e[0], e[1]);
            bool isMst = mstSet.Contains((long)a * 10000 + b);

            Vector3 posA = graph.Nodes[e[0]].Position;
            Vector3 posB = graph.Nodes[e[1]].Position;
            Vector3 mid = (posA + posB) * 0.5f;
            float edgeLen = Vector3.Distance(posA, posB);

            // Вычисляем перпендикулярное смещение для кривизны
            // Направление ребра в XZ
            Vector3 dir = posB - posA;
            Vector3 perpXZ = new Vector3(-dir.z, 0f, dir.x).normalized;

            // Случайное боковое смещение: 10–25% длины ребра
            float lateralOffset = edgeLen * (0.1f + (float)rng.NextDouble() * 0.15f);
            if (rng.NextDouble() < 0.5) lateralOffset = -lateralOffset;

            // Случайное вертикальное смещение (небольшое)
            float verticalOffset = ((float)rng.NextDouble() * 2f - 1f) * config.HeightVariation * 0.3f;

            Vector3 midpoint = mid + perpXZ * lateralOffset + Vector3.up * verticalOffset;

            // Clamp midpoint внутри мира
            midpoint.x = Mathf.Clamp(midpoint.x, margin, worldSize.x - margin);
            midpoint.y = Mathf.Clamp(midpoint.y, margin, worldSize.y - margin);
            midpoint.z = Mathf.Clamp(midpoint.z, margin, worldSize.z - margin);

            graph.Edges.Add(new CaveEdge
            {
                NodeA = e[0],
                NodeB = e[1],
                Radius = config.TunnelRadius,
                IsMST = isMst,
                HasMidpoint = true,
                Midpoint = midpoint
            });
        }

        // Degree
        foreach (var edge in graph.Edges)
        {
            graph.Nodes[edge.NodeA].Degree++;
            graph.Nodes[edge.NodeB].Degree++;
        }

        // --- 5. Роли ---
        AssignNodeRoles(graph, config, worldSize, rng);

        return graph;
    }

    // =========================================================================
    //  Роли
    // =========================================================================

    private static void AssignNodeRoles(CaveGraph graph, CaveGenerationConfig config,
        Vector3 worldSize, System.Random rng)
    {
        var nodes = graph.Nodes;
        Vector3 center = worldSize * 0.5f;

        for (int i = 0; i < nodes.Count; i++)
        {
            var n = nodes[i];
            if (n.Degree <= 1) { n.Type = CaveNodeType.Deadend; n.Radius = config.DeadendRadius; }
            else if (n.Degree >= 3) { n.Type = CaveNodeType.Hall; n.Radius = config.HallRadius; }
            else { n.Type = CaveNodeType.Tunnel; n.Radius = config.TunnelRadius; }
        }

        // Spawn — ближайший к центру
        float bestD = float.MaxValue;
        int spawnIdx = 0;
        for (int i = 0; i < nodes.Count; i++)
        {
            float d = Vector3.SqrMagnitude(nodes[i].Position - center);
            if (d < bestD) { bestD = d; spawnIdx = i; }
        }
        nodes[spawnIdx].Type = CaveNodeType.Spawn;
        nodes[spawnIdx].Radius = config.SpawnRadius;
        graph.SpawnIndex = spawnIdx;

        // Extract — максимально далеко от спавна
        float bestExtD = 0;
        int extIdx = -1;
        for (int i = 0; i < nodes.Count; i++)
        {
            if (i == spawnIdx) continue;
            float d = Vector3.SqrMagnitude(nodes[i].Position - nodes[spawnIdx].Position);
            if (d > bestExtD) { bestExtD = d; extIdx = i; }
        }
        if (extIdx >= 0)
        {
            nodes[extIdx].Type = CaveNodeType.Extract;
            nodes[extIdx].Radius = config.ExtractRadius;
            graph.ExtractIndex = extIdx;
        }

        // Objectives
        var candidates = new List<(int idx, float dist)>();
        for (int i = 0; i < nodes.Count; i++)
        {
            if (i == spawnIdx || i == extIdx) continue;
            if (nodes[i].Type == CaveNodeType.Hall || nodes[i].Type == CaveNodeType.Deadend)
            {
                float d = Vector3.SqrMagnitude(nodes[i].Position - nodes[spawnIdx].Position);
                candidates.Add((i, d));
            }
        }
        candidates.Sort((a, b) => b.dist.CompareTo(a.dist));

        int objCount = Mathf.Min(2 + rng.Next(2), candidates.Count);
        for (int j = 0; j < objCount; j++)
        {
            int idx = candidates[j].idx;
            nodes[idx].Type = CaveNodeType.Objective;
            nodes[idx].Radius = config.ObjectiveRadius;
            graph.ObjectiveIndices.Add(idx);
        }

        // Рёбра у широких узлов — чуть шире
        foreach (var edge in graph.Edges)
        {
            bool wide = IsWideType(nodes[edge.NodeA].Type) || IsWideType(nodes[edge.NodeB].Type);
            if (wide) edge.Radius = config.TunnelRadius * 1.3f;
        }
    }

    private static bool IsWideType(CaveNodeType t) =>
        t == CaveNodeType.Hall || t == CaveNodeType.Spawn ||
        t == CaveNodeType.Extract || t == CaveNodeType.Objective;

    // =========================================================================
    //  Poisson Disk 2D
    // =========================================================================

    private static List<Vector2> PoissonDisk2D(float w, float h, float minD,
        int maxPts, float margin, System.Random rng)
    {
        var pts = new List<Vector2>();
        float cell = minD / Mathf.Sqrt(2f);
        int gW = Mathf.CeilToInt(w / cell), gH = Mathf.CeilToInt(h / cell);
        var grid = new int[gW * gH];
        for (int i = 0; i < grid.Length; i++) grid[i] = -1;
        var active = new List<int>();

        void Add(float x, float y)
        {
            int idx = pts.Count;
            pts.Add(new Vector2(x, y));
            active.Add(idx);
            int gi = Mathf.Clamp((int)(x / cell), 0, gW - 1);
            int gj = Mathf.Clamp((int)(y / cell), 0, gH - 1);
            grid[gi + gj * gW] = idx;
        }

        Add(margin + (float)rng.NextDouble() * (w - 2 * margin),
            margin + (float)rng.NextDouble() * (h - 2 * margin));

        while (active.Count > 0 && pts.Count < maxPts)
        {
            int ai = rng.Next(active.Count);
            var pt = pts[active[ai]];
            bool found = false;

            for (int att = 0; att < 30; att++)
            {
                float angle = (float)rng.NextDouble() * Mathf.PI * 2f;
                float dist = minD + (float)rng.NextDouble() * minD;
                float nx = pt.x + Mathf.Cos(angle) * dist;
                float ny = pt.y + Mathf.Sin(angle) * dist;

                if (nx < margin || nx > w - margin || ny < margin || ny > h - margin) continue;

                int gx = (int)(nx / cell), gy = (int)(ny / cell);
                bool ok = true;
                for (int di = -2; di <= 2 && ok; di++)
                for (int dj = -2; dj <= 2 && ok; dj++)
                {
                    int ni = gx + di, nj = gy + dj;
                    if (ni < 0 || ni >= gW || nj < 0 || nj >= gH) continue;
                    int idx = grid[ni + nj * gW];
                    if (idx >= 0)
                    {
                        float dx = pts[idx].x - nx, dy = pts[idx].y - ny;
                        if (dx * dx + dy * dy < minD * minD) ok = false;
                    }
                }

                if (ok) { Add(nx, ny); found = true; break; }
            }

            if (!found) active.RemoveAt(ai);
        }

        return pts;
    }

    // =========================================================================
    //  Delaunay 2D (Bowyer-Watson)
    // =========================================================================

    private static List<int[]> Delaunay2D(List<Vector2> points)
    {
        var sA = new Vector2(-10000f, -10000f);
        var sB = new Vector2(30000f, -10000f);
        var sC = new Vector2(10000f, 30000f);

        var all = new List<Vector2> { sA, sB, sC };
        all.AddRange(points);

        var tris = new List<int[]> { new[] { 0, 1, 2 } };

        for (int i = 3; i < all.Count; i++)
        {
            var p = all[i];
            var bad = new List<int>();
            for (int t = 0; t < tris.Count; t++)
            {
                var tri = tris[t];
                if (InCircumcircle(all[tri[0]], all[tri[1]], all[tri[2]], p))
                    bad.Add(t);
            }

            var boundary = new List<int[]>();
            foreach (int t in bad)
            {
                var tri = tris[t];
                for (int j = 0; j < 3; j++)
                {
                    int ea = tri[j], eb = tri[(j + 1) % 3];
                    bool shared = bad.Any(t2 => t2 != t &&
                        tris[t2].Contains(ea) && tris[t2].Contains(eb));
                    if (!shared) boundary.Add(new[] { ea, eb });
                }
            }

            bad.Sort();
            for (int j = bad.Count - 1; j >= 0; j--) tris.RemoveAt(bad[j]);
            foreach (var e in boundary) tris.Add(new[] { e[0], e[1], i });
        }

        return tris
            .Where(t => t[0] >= 3 && t[1] >= 3 && t[2] >= 3)
            .Select(t => new[] { t[0] - 3, t[1] - 3, t[2] - 3 })
            .ToList();
    }

    private static bool InCircumcircle(Vector2 a, Vector2 b, Vector2 c, Vector2 p)
    {
        float ax = a.x - p.x, ay = a.y - p.y;
        float bx = b.x - p.x, by = b.y - p.y;
        float cx = c.x - p.x, cy = c.y - p.y;
        return (ax * ax + ay * ay) * (bx * cy - cx * by)
             - (bx * bx + by * by) * (ax * cy - cx * ay)
             + (cx * cx + cy * cy) * (ax * by - bx * ay) > 0;
    }

    // =========================================================================
    //  MST (Kruskal)
    // =========================================================================

    private static List<int[]> Kruskal(List<CaveNode> nodes, List<int[]> edges)
    {
        int n = nodes.Count;
        var parent = new int[n];
        for (int i = 0; i < n; i++) parent[i] = i;
        int Find(int x) => parent[x] == x ? x : (parent[x] = Find(parent[x]));

        var sorted = edges.OrderBy(e =>
            Vector3.SqrMagnitude(nodes[e[0]].Position - nodes[e[1]].Position)).ToList();

        var tree = new List<int[]>();
        foreach (var e in sorted)
        {
            int ra = Find(e[0]), rb = Find(e[1]);
            if (ra != rb) { tree.Add(e); parent[ra] = rb; }
        }
        return tree;
    }

    private static void Shuffle<T>(List<T> list, System.Random rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}