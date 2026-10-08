using System.Collections.Generic;
using UnityEngine;

namespace CaveGraphBuilder
{
    public enum EdgeType
    {
        Normal, // обычный туннель, проходим в обе стороны
        Drop,   // обрыв: только сверху вниз
        Crawl   // лаз: проходим только пригнувшись
    }

    public class CaveNode
    {
        public Vector3 Position;
        public float Radius;
        public LevelInfo.RegionRole Role = LevelInfo.RegionRole.None;
        public string SideMissionId = "";

        /// <summary>Расстояние от старта по графу в метрах (с учётом извилистости туннелей).</summary>
        public float Distance;
        /// <summary>То же, нормированное: 0 = старт, 1 = самый дальний регион.</summary>
        public float Distance01;
    }

    public class CaveEdge
    {
        public int A, B;
        public EdgeType Type;
        /// <summary>Ребро добавлено поверх остова (создаёт петлю).</summary>
        public bool IsLoop;
        public float Length;

        public int Other(int node) => node == A ? B : A;

        /// <summary>Можно ли пройти ребро из узла from. Обрыв проходится только вниз.</summary>
        public bool CanTraverse(int from, List<CaveNode> nodes)
        {
            if (Type != EdgeType.Drop) return true;
            return nodes[from].Position.y >= nodes[Other(from)].Position.y;
        }
    }

    /// <summary>
    /// Граф регионов уровня: результат этапа 1 пайплайна.
    /// </summary>
    public class CaveGraph
    {
        public readonly List<CaveNode> Nodes = new();
        public readonly List<CaveEdge> Edges = new();
        public List<int>[] Adjacency; // индексы рёбер, инцидентных узлу
        public int Start = -1;

        /// <summary>Оценка длительности уровня в секундах.</summary>
        public float EstimatedDuration;

        public CaveGraph(List<Vector3> positions)
        {
            foreach (var p in positions)
                Nodes.Add(new CaveNode { Position = p });

            Adjacency = new List<int>[Nodes.Count];
            for (int i = 0; i < Adjacency.Length; i++)
                Adjacency[i] = new List<int>();
        }

        public CaveEdge AddEdge(int a, int b, EdgeType type, bool isLoop)
        {
            var e = new CaveEdge
            {
                A = a, B = b, Type = type, IsLoop = isLoop,
                Length = Vector3.Distance(Nodes[a].Position, Nodes[b].Position)
            };
            Adjacency[a].Add(Edges.Count);
            Adjacency[b].Add(Edges.Count);
            Edges.Add(e);
            return e;
        }

        /// <summary>Убирает последнее добавленное ребро (для пробных добавлений при ремонте).</summary>
        public void RemoveLastEdge()
        {
            int ei = Edges.Count - 1;
            var e = Edges[ei];
            Adjacency[e.A].Remove(ei);
            Adjacency[e.B].Remove(ei);
            Edges.RemoveAt(ei);
        }

        public bool HasEdge(int a, int b)
        {
            foreach (int ei in Adjacency[a])
                if (Edges[ei].Other(a) == b)
                    return true;
            return false;
        }

        /// <summary>Число входов в регион.</summary>
        public int Degree(int node) => Adjacency[node].Count;

        /// <summary>
        /// Кратчайшие расстояния от узла по проходимым рёбрам.
        /// walkOnly: только туннели, проходимые в обе стороны (без обрывов).
        /// </summary>
        public float[] Dijkstra(int source, float winding, bool walkOnly = false)
        {
            int n = Nodes.Count;
            var dist = new float[n];
            var done = new bool[n];
            for (int i = 0; i < n; i++) dist[i] = float.PositiveInfinity;
            dist[source] = 0f;

            // узлов мало (десятки), поэтому простой O(n^2) без кучи
            for (int iter = 0; iter < n; iter++)
            {
                int u = -1;
                for (int i = 0; i < n; i++)
                    if (!done[i] && (u < 0 || dist[i] < dist[u]))
                        u = i;

                if (u < 0 || float.IsInfinity(dist[u])) break;
                done[u] = true;

                foreach (int ei in Adjacency[u])
                {
                    var e = Edges[ei];
                    if (walkOnly && e.Type == EdgeType.Drop) continue;
                    if (!e.CanTraverse(u, Nodes)) continue;

                    int v = e.Other(u);
                    float nd = dist[u] + e.Length * winding;
                    if (nd < dist[v]) dist[v] = nd;
                }
            }

            return dist;
        }

        /// <summary>Пересчитывает Distance и Distance01 у всех узлов от старта.</summary>
        public void UpdateDistances(float winding)
        {
            var dist = Dijkstra(Start, winding);
            float max = 0f;
            foreach (float d in dist)
                if (!float.IsInfinity(d) && d > max) max = d;

            for (int i = 0; i < Nodes.Count; i++)
            {
                Nodes[i].Distance = dist[i];
                Nodes[i].Distance01 = max > 0f ? dist[i] / max : 0f;
            }
        }

        /// <summary>
        /// Мосты по рёбрам, проходимым в обе стороны: рёбра, без которых граф распадается.
        /// Регион лежит на петле, если у него есть инцидентное ребро, которое не мост.
        /// </summary>
        public bool[] FindBridges()
        {
            int n = Nodes.Count;
            var bridges = new bool[Edges.Count];
            var tin = new int[n];
            var low = new int[n];
            for (int i = 0; i < n; i++) tin[i] = -1;
            int timer = 0;

            // итеративный DFS, чтобы не упираться в глубину рекурсии
            var stack = new Stack<(int node, int parentEdge, int next)>();
            for (int root = 0; root < n; root++)
            {
                if (tin[root] >= 0) continue;
                stack.Push((root, -1, 0));
                tin[root] = low[root] = timer++;

                while (stack.Count > 0)
                {
                    var (u, pe, next) = stack.Pop();
                    if (next < Adjacency[u].Count)
                    {
                        stack.Push((u, pe, next + 1));
                        int ei = Adjacency[u][next];
                        if (ei == pe || Edges[ei].Type == EdgeType.Drop) continue;

                        int v = Edges[ei].Other(u);
                        if (tin[v] < 0)
                        {
                            tin[v] = low[v] = timer++;
                            stack.Push((v, ei, 0));
                        }
                        else
                        {
                            low[u] = Mathf.Min(low[u], tin[v]);
                        }
                    }
                    else if (pe >= 0)
                    {
                        int p = Edges[pe].Other(u);
                        low[p] = Mathf.Min(low[p], low[u]);
                        if (low[u] > tin[p])
                            bridges[pe] = true;
                    }
                }
            }

            return bridges;
        }

        public bool IsOnLoop(int node, bool[] bridges)
        {
            foreach (int ei in Adjacency[node])
                if (Edges[ei].Type != EdgeType.Drop && !bridges[ei])
                    return true;
            return false;
        }

        /// <summary>
        /// Есть ли два непересекающихся по рёбрам пути от старта до узла
        /// (туда и обратно разными дорогами). Это так, если на пути нет ни одного моста.
        /// </summary>
        public bool HasTwoDisjointPaths(int node, bool[] bridges)
        {
            if (node == Start) return true;

            // BFS от старта, не переходя через мосты и обрывы
            var visited = new bool[Nodes.Count];
            var queue = new Queue<int>();
            queue.Enqueue(Start);
            visited[Start] = true;

            while (queue.Count > 0)
            {
                int u = queue.Dequeue();
                if (u == node) return true;
                foreach (int ei in Adjacency[u])
                {
                    var e = Edges[ei];
                    if (bridges[ei] || e.Type == EdgeType.Drop) continue;
                    int v = e.Other(u);
                    if (visited[v]) continue;
                    visited[v] = true;
                    queue.Enqueue(v);
                }
            }

            return false;
        }

        public List<int> NodesWithRole(LevelInfo.RegionRole role)
        {
            var result = new List<int>();
            for (int i = 0; i < Nodes.Count; i++)
                if (Nodes[i].Role == role)
                    result.Add(i);
            return result;
        }

        public Bounds GetBounds()
        {
            if (Nodes.Count == 0) return new Bounds();
            var b = new Bounds(Nodes[0].Position, Vector3.zero);
            foreach (var n in Nodes)
                b.Encapsulate(new Bounds(n.Position, Vector3.one * (n.Radius * 2f)));
            return b;
        }
    }
}
