using System;
using System.Collections.Generic;
using CaveGraphBuilder;
using UnityEngine;

public enum SurfaceClass : byte
{
    None,
    Floor,
    Wall,
    Ceiling
}

/// <summary>Правила передвижения игрока в вокселях.</summary>
public struct PlayerRules
{
    public int JumpUp;      // на сколько вокселей можно подняться (ступенька или прыжок)
    public int MaxFall;     // с какой высоты можно спрыгнуть
    public int Clearance;   // сколько вокселей воздуха нужно над полом (рост в приседе)
    public float FloorCos;  // косинус максимального уклона пола

    public static PlayerRules From(LevelAnalysisSettings a, SdfSceneSettings s, MovementConfig m, float voxelSize)
    {
        float jump = m != null && m.Gravity > 0f ? m.BaseJumpForce * m.BaseJumpForce / (2f * m.Gravity) : 2f;
        float crouch = m != null ? m.CrouchHeightMultiplier : 0.5f;
        float slope = (m != null ? m.MaxSlopeAngle : 45f) + a.slopeTolerance;

        return new PlayerRules
        {
            JumpUp = Mathf.Max(1, Mathf.FloorToInt(jump / voxelSize + 1e-3f)),
            MaxFall = Mathf.Max(1, Mathf.FloorToInt(a.maxFallHeight / voxelSize + 1e-3f)),
            Clearance = Mathf.Max(1, Mathf.CeilToInt(s.playerHeight * crouch / voxelSize - 1e-3f)),
            FloorCos = Mathf.Cos(Mathf.Min(slope, 89f) * Mathf.Deg2Rad)
        };
    }
}

/// <summary>Результат этапа 4.</summary>
public class LevelAnalysis
{
    public bool Success;
    /// <summary>Отказ нельзя починить ремонтом туннелей — нужен новый граф.</summary>
    public bool Topological;
    public readonly List<string> Problems = new();

    /// <summary>Расстояние от воздуха до ближайшего камня, м (у камня 0).</summary>
    public float[] Edt;
    public byte[] Surface;
    public bool[] Standable;
    /// <summary>Достижимо от спавна.</summary>
    public bool[] Reachable;
    /// <summary>Отсюда можно дойти до спавна (null, если не проверялось).</summary>
    public bool[] CanReturn;
    public int SpawnIndex = -1;

    public readonly List<int> UnreachableNodes = new();
    public readonly List<int> FailedEdges = new();
    /// <summary>Самое узкое место туннеля: EDT на высоте головы, м.</summary>
    public readonly Dictionary<int, float> EdgeBottleneck = new();

    public int StandableCount, ReachableCount;
}

/// <summary>
/// Этап 4 пайплайна: EDT, классификация поверхностей, проходимость по полу с правилами игрока,
/// проверка каждого туннеля. Поле не меняет — это делает Cleanup после удачной проверки.
/// </summary>
public static class LevelAnalyzer
{
    private static readonly int[] DX = { 1, -1, 0, 0, 1, 1, -1, -1 };
    private static readonly int[] DZ = { 0, 0, 1, -1, 1, -1, 1, -1 };

    public static LevelAnalysis Analyze(VoxelField f, CaveGraph graph, PlayerRules rules, bool requireReturn)
    {
        var a = new LevelAnalysis
        {
            Edt = ComputeEdt(f),
            Surface = ClassifySurfaces(f, rules.FloorCos)
        };
        a.Standable = FindStandable(f, a.Surface, rules, out a.StandableCount);

        int n = graph.Nodes.Count;
        a.SpawnIndex = FindSpawn(f, graph, a.Standable);
        if (a.SpawnIndex < 0)
        {
            a.Topological = true;
            a.Problems.Add("в стартовой комнате нет пола, на котором можно стоять");
            return a;
        }

        a.Reachable = Flood(f, a.Standable, rules, a.SpawnIndex, reverse: false);
        if (requireReturn)
            a.CanReturn = Flood(f, a.Standable, rules, a.SpawnIndex, reverse: true);

        // Комнаты: есть ли в каждой достижимый пол и путь обратно
        var reached = new bool[n];
        var returns = new bool[n];
        for (int i = 0; i < a.Standable.Length; i++)
        {
            if (!a.Standable[i]) continue;
            int room = f.Labels[i] - 1;
            if (room < 0 || room >= n) continue;
            if (a.Reachable[i]) { reached[room] = true; a.ReachableCount++; }
            if (a.CanReturn != null && a.CanReturn[i]) returns[room] = true;
        }

        for (int node = 0; node < n; node++)
        {
            if (!reached[node])
            {
                a.UnreachableNodes.Add(node);
                a.Problems.Add($"комната {node} ({graph.Nodes[node].Role}) недостижима от старта");
            }
            else if (requireReturn && !returns[node])
            {
                a.UnreachableNodes.Add(node);
                a.Problems.Add($"из комнаты {node} ({graph.Nodes[node].Role}) нельзя вернуться к старту");
            }
        }

        // Каждый туннель по отдельности: от комнаты до комнаты только по нему
        var cellsByLabel = new Dictionary<ushort, List<int>>();
        for (int i = 0; i < a.Standable.Length; i++)
        {
            if (!a.Standable[i]) continue;
            ushort label = f.Labels[i];
            if (!cellsByLabel.TryGetValue(label, out var list))
                cellsByLabel[label] = list = new List<int>();
            list.Add(i);
        }

        for (int ei = 0; ei < graph.Edges.Count; ei++)
        {
            var e = graph.Edges[ei];
            var edgeLabel = (ushort)(n + ei + 1);
            var ra = (ushort)(e.A + 1);
            var rb = (ushort)(e.B + 1);
            var allowed = new HashSet<ushort> { edgeLabel, ra, rb };

            bool ok;
            if (e.Type == EdgeType.Drop)
            {
                bool aUpper = graph.Nodes[e.A].Position.y >= graph.Nodes[e.B].Position.y;
                ok = aUpper
                    ? ReachRestricted(f, a.Standable, rules, cellsByLabel, ra, rb, allowed)
                    : ReachRestricted(f, a.Standable, rules, cellsByLabel, rb, ra, allowed);
            }
            else
            {
                ok = ReachRestricted(f, a.Standable, rules, cellsByLabel, ra, rb, allowed)
                     && ReachRestricted(f, a.Standable, rules, cellsByLabel, rb, ra, allowed);
            }

            if (!ok)
            {
                a.FailedEdges.Add(ei);
                a.Problems.Add($"туннель {ei} ({e.Type}) между {e.A} и {e.B} непроходим");
            }

            if (cellsByLabel.TryGetValue(edgeLabel, out var edgeCells))
                a.EdgeBottleneck[ei] = Bottleneck(f, a.Edt, edgeCells);
        }

        a.Success = a.UnreachableNodes.Count == 0 && a.FailedEdges.Count == 0;
        // Комнаты недостижимы, но все туннели проходимы — ремонтом туннелей это не исправить
        if (!a.Success && a.FailedEdges.Count == 0)
            a.Topological = true;

        return a;
    }

    // ---------- EDT (Felzenszwalb–Huttenlocher) ----------

    private const double Inf = 1e9;

    /// <summary>Евклидово расстояние от каждого воздушного сэмпла до ближайшего камня, м.</summary>
    public static float[] ComputeEdt(VoxelField f)
    {
        int sx = f.Size.x, sy = f.Size.y, sz = f.Size.z;
        var g = new double[f.Count];
        for (int i = 0; i < g.Length; i++) g[i] = f.IsAir(i) ? Inf : 0.0;

        int maxN = Mathf.Max(sx, Mathf.Max(sy, sz));
        var line = new double[maxN];
        var res = new double[maxN];
        var v = new int[maxN];
        var zb = new double[maxN + 1];

        // по X
        for (int z = 0; z < sz; z++)
        for (int y = 0; y < sy; y++)
            Pass(g, f.Index(0, y, z), 1, sx, line, res, v, zb);
        // по Y
        for (int z = 0; z < sz; z++)
        for (int x = 0; x < sx; x++)
            Pass(g, f.Index(x, 0, z), sx, sy, line, res, v, zb);
        // по Z
        for (int y = 0; y < sy; y++)
        for (int x = 0; x < sx; x++)
            Pass(g, f.Index(x, y, 0), sx * sy, sz, line, res, v, zb);

        var edt = new float[g.Length];
        for (int i = 0; i < g.Length; i++)
            edt[i] = (float)Math.Sqrt(Math.Min(g[i], Inf)) * f.VoxelSize;
        return edt;
    }

    private static void Pass(double[] g, int start, int stride, int n, double[] line, double[] res, int[] v, double[] zb)
    {
        for (int i = 0; i < n; i++) line[i] = g[start + i * stride];
        Dt1D(line, n, res, v, zb);
        for (int i = 0; i < n; i++) g[start + i * stride] = res[i];
    }

    /// <summary>Одномерное преобразование расстояний: нижняя огибающая парабол.</summary>
    private static void Dt1D(double[] f, int n, double[] d, int[] v, double[] z)
    {
        int k = 0;
        v[0] = 0;
        z[0] = double.NegativeInfinity;
        z[1] = double.PositiveInfinity;

        for (int q = 1; q < n; q++)
        {
            double s = ((f[q] + (double)q * q) - (f[v[k]] + (double)v[k] * v[k])) / (2.0 * q - 2.0 * v[k]);
            while (s <= z[k])
            {
                k--;
                s = ((f[q] + (double)q * q) - (f[v[k]] + (double)v[k] * v[k])) / (2.0 * q - 2.0 * v[k]);
            }
            k++;
            v[k] = q;
            z[k] = s;
            z[k + 1] = double.PositiveInfinity;
        }

        k = 0;
        for (int q = 0; q < n; q++)
        {
            while (z[k + 1] < q) k++;
            double dq = q - v[k];
            d[q] = dq * dq + f[v[k]];
        }
    }

    // ---------- поверхности ----------

    /// <summary>
    /// Для воздушных сэмплов у камня: нормаль по градиенту плотности и класс — пол, стена или потолок.
    /// </summary>
    public static byte[] ClassifySurfaces(VoxelField f, float floorCos)
    {
        var result = new byte[f.Count];
        int sx = f.Size.x, sy = f.Size.y, sz = f.Size.z;

        for (int z = 0; z < sz; z++)
        for (int y = 0; y < sy; y++)
        for (int x = 0; x < sx; x++)
        {
            int i = f.Index(x, y, z);
            if (!f.IsAir(i)) continue;
            if (f.IsAir(x + 1, y, z) && f.IsAir(x - 1, y, z) && f.IsAir(x, y + 1, z) &&
                f.IsAir(x, y - 1, z) && f.IsAir(x, y, z + 1) && f.IsAir(x, y, z - 1))
                continue;

            // Плотность растёт к камню, поэтому нормаль поверхности (в сторону воздуха) — минус градиент
            var n = -new Vector3(
                D(f, x + 1, y, z) - D(f, x - 1, y, z),
                D(f, x, y + 1, z) - D(f, x, y - 1, z),
                D(f, x, y, z + 1) - D(f, x, y, z - 1));

            if (n.sqrMagnitude < 1e-12f)
            {
                result[i] = (byte)SurfaceClass.Wall;
                continue;
            }
            n.Normalize();

            if (n.y >= floorCos) result[i] = (byte)SurfaceClass.Floor;
            else if (n.y <= -0.5f) result[i] = (byte)SurfaceClass.Ceiling;
            else result[i] = (byte)SurfaceClass.Wall;
        }

        return result;
    }

    private static float D(VoxelField f, int x, int y, int z)
    {
        x = Mathf.Clamp(x, 0, f.Size.x - 1);
        y = Mathf.Clamp(y, 0, f.Size.y - 1);
        z = Mathf.Clamp(z, 0, f.Size.z - 1);
        return Mathf.Clamp(f.Density[f.Index(x, y, z)], -VoxelField.SolidDensity, VoxelField.SolidDensity);
    }

    // ---------- проходимость ----------

    /// <summary>Где можно стоять: воздух над камнем, пол не круче допустимого, над головой хватает места.</summary>
    public static bool[] FindStandable(VoxelField f, byte[] surface, PlayerRules rules, out int count)
    {
        var result = new bool[f.Count];
        count = 0;
        for (int z = 0; z < f.Size.z; z++)
        for (int y = 1; y < f.Size.y; y++)
        for (int x = 0; x < f.Size.x; x++)
        {
            int i = f.Index(x, y, z);
            if (surface[i] != (byte)SurfaceClass.Floor) continue;
            if (f.IsAir(x, y - 1, z)) continue;
            if (!ColumnAir(f, x, z, y, y + rules.Clearance - 1)) continue;
            result[i] = true;
            count++;
        }
        return result;
    }

    /// <summary>Спавн — точка пола в стартовой комнате, ближайшая к узлу старта.</summary>
    private static int FindSpawn(VoxelField f, CaveGraph graph, bool[] standable)
    {
        var startLabel = (ushort)(graph.Start + 1);
        Vector3 target = graph.Nodes[graph.Start].Position;
        int best = -1;
        float bestDist = float.MaxValue;

        for (int i = 0; i < standable.Length; i++)
        {
            if (!standable[i] || f.Labels[i] != startLabel) continue;
            float d = (f.ToLocal(i) - target).sqrMagnitude;
            if (d < bestDist)
            {
                bestDist = d;
                best = i;
            }
        }
        return best;
    }

    /// <summary>
    /// Можно ли перейти с пола u на пол v в соседней колонке: подняться не выше прыжка,
    /// спрыгнуть не выше maxFall, и по пути над головой есть место.
    /// </summary>
    private static bool CanMove(VoxelField f, PlayerRules r, int ux, int uy, int uz, int vx, int vy, int vz)
    {
        int dy = vy - uy;
        if (dy > r.JumpUp || dy < -r.MaxFall) return false;
        int h = r.Clearance;

        if (dy >= 0)
            return ColumnAir(f, ux, uz, uy, vy + h - 1) && ColumnAir(f, vx, vz, vy, vy + h - 1);

        // при спуске над целью должно быть свободно до уровня, с которого прыгаем
        return ColumnAir(f, vx, vz, vy, uy + h - 1);
    }

    private static bool ColumnAir(VoxelField f, int x, int z, int y0, int y1)
    {
        if (y1 >= f.Size.y) return false;
        for (int y = Mathf.Max(0, y0); y <= y1; y++)
            if (!f.IsAir(f.Index(x, y, z)))
                return false;
        return true;
    }

    /// <summary>
    /// Обход по полу. reverse = false: куда можно дойти от seed; true: откуда можно дойти до seed.
    /// </summary>
    private static bool[] Flood(VoxelField f, bool[] standable, PlayerRules r, int seed, bool reverse)
    {
        var visited = new bool[f.Count];
        var queue = new Queue<int>();
        visited[seed] = true;
        queue.Enqueue(seed);

        while (queue.Count > 0)
        {
            int cur = queue.Dequeue();
            var c = f.Coord(cur);
            ForEachMove(f, standable, r, c, reverse, j =>
            {
                if (visited[j]) return false;
                visited[j] = true;
                queue.Enqueue(j);
                return false;
            });
        }

        return visited;
    }

    /// <summary>Обход по полу только по клеткам с разрешёнными метками: дойдёт ли от метки from до метки to.</summary>
    private static bool ReachRestricted(VoxelField f, bool[] standable, PlayerRules r,
        Dictionary<ushort, List<int>> cellsByLabel, ushort from, ushort to, HashSet<ushort> allowed)
    {
        if (!cellsByLabel.TryGetValue(from, out var seeds) || !cellsByLabel.ContainsKey(to))
            return false;

        var visited = new HashSet<int>(seeds);
        var queue = new Queue<int>(seeds);
        bool found = false;

        while (queue.Count > 0 && !found)
        {
            var c = f.Coord(queue.Dequeue());
            ForEachMove(f, standable, r, c, false, j =>
            {
                ushort label = f.Labels[j];
                if (!allowed.Contains(label) || !visited.Add(j)) return false;
                if (label == to) { found = true; return true; }
                queue.Enqueue(j);
                return false;
            });
        }

        return found;
    }

    /// <summary>Перебирает соседние клетки пола, куда (или откуда при reverse) можно перейти. visit → true = стоп.</summary>
    private static void ForEachMove(VoxelField f, bool[] standable, PlayerRules r, Vector3Int c, bool reverse,
        Func<int, bool> visit)
    {
        for (int d = 0; d < 8; d++)
        {
            int nx = c.x + DX[d], nz = c.z + DZ[d];
            if (nx < 0 || nz < 0 || nx >= f.Size.x || nz >= f.Size.z) continue;

            // вперёд: цель от c.y - MaxFall до c.y + JumpUp; назад: источник от c.y - JumpUp до c.y + MaxFall
            int yMin = reverse ? c.y - r.JumpUp : c.y - r.MaxFall;
            int yMax = reverse ? c.y + r.MaxFall : c.y + r.JumpUp;

            for (int ny = Mathf.Max(1, yMin); ny <= Mathf.Min(f.Size.y - 1, yMax); ny++)
            {
                int j = f.Index(nx, ny, nz);
                if (!standable[j]) continue;

                bool can = reverse
                    ? CanMove(f, r, nx, ny, nz, c.x, c.y, c.z)
                    : CanMove(f, r, c.x, c.y, c.z, nx, ny, nz);
                if (can && visit(j)) return;
            }
        }
    }

    /// <summary>Самое узкое место среди клеток туннеля: расстояние до стены на высоте головы.</summary>
    private static float Bottleneck(VoxelField f, float[] edt, List<int> cells)
    {
        float min = float.MaxValue;
        foreach (int i in cells)
        {
            var c = f.Coord(i);
            int head = c.y + 1 < f.Size.y ? f.Index(c.x, c.y + 1, c.z) : i;
            min = Mathf.Min(min, edt[head]);
        }
        return min;
    }

    // ---------- очистка ----------

    /// <summary>
    /// После удачной проверки: недостижимые полости воздуха заливаются камнем,
    /// висящие куски камня (не связанные со стенами мира) убираются.
    /// </summary>
    public static (int pockets, int floating) Cleanup(VoxelField f, int spawn)
    {
        int sx = f.Size.x, sy = f.Size.y, sz = f.Size.z;

        // Воздух, связанный со спавном (6-связность)
        var air = new bool[f.Count];
        var queue = new Queue<int>();
        air[spawn] = true;
        queue.Enqueue(spawn);
        while (queue.Count > 0)
        {
            var c = f.Coord(queue.Dequeue());
            Visit6(f, c, j => f.IsAir(j), air, queue);
        }

        int pockets = 0;
        for (int i = 0; i < f.Count; i++)
        {
            if (f.IsAir(i) && !air[i])
            {
                f.Density[i] = 1f;
                f.Labels[i] = 0;
                pockets++;
            }
        }

        // Камень, связанный с границей мира
        var solid = new bool[f.Count];
        for (int z = 0; z < sz; z++)
        for (int y = 0; y < sy; y++)
        for (int x = 0; x < sx; x++)
        {
            if (x != 0 && y != 0 && z != 0 && x != sx - 1 && y != sy - 1 && z != sz - 1) continue;
            int i = f.Index(x, y, z);
            if (f.IsAir(i) || solid[i]) continue;
            solid[i] = true;
            queue.Enqueue(i);
        }
        while (queue.Count > 0)
        {
            var c = f.Coord(queue.Dequeue());
            Visit6(f, c, j => !f.IsAir(j), solid, queue);
        }

        int floating = 0;
        for (int i = 0; i < f.Count; i++)
        {
            if (!f.IsAir(i) && !solid[i])
            {
                f.Density[i] = -1f;
                floating++;
            }
        }

        return (pockets, floating);
    }

    private static void Visit6(VoxelField f, Vector3Int c, Func<int, bool> pass, bool[] visited, Queue<int> queue)
    {
        Try(c.x + 1, c.y, c.z); Try(c.x - 1, c.y, c.z);
        Try(c.x, c.y + 1, c.z); Try(c.x, c.y - 1, c.z);
        Try(c.x, c.y, c.z + 1); Try(c.x, c.y, c.z - 1);

        void Try(int x, int y, int z)
        {
            if (!f.InBounds(x, y, z)) return;
            int j = f.Index(x, y, z);
            if (visited[j] || !pass(j)) return;
            visited[j] = true;
            queue.Enqueue(j);
        }
    }
}
