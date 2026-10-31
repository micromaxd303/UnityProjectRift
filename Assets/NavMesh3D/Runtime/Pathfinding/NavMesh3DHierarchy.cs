using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Mathematics;

namespace NavMesh3DSystem
{
    /// <summary>
    /// Абстрактный граф для HPA*.
    ///
    /// УЗЛЫ ГРАФА — «двери»: на каждой грани между двумя соседними компонентами
    /// (связный кусок воздуха внутри чанка) выбирается одна пара узлов — по одному
    /// с каждой стороны, ближе всего к середине общего проёма. Стороны двери связаны
    /// ребром стоимостью 1 (соседние узлы по оси).
    ///
    /// РЁБРА ВНУТРИ КОМПОНЕНТЫ — длины кратчайших путей между всеми её дверями,
    /// посчитанные при запекании поиском, не выходящим за чанк (не больше 4096 узлов).
    ///
    /// Поиск пути тогда — A* по нескольким сотням/тысячам дверей вместо миллиона узлов,
    /// а уточнение по сетке идёт кусками внутри одного чанка.
    /// </summary>
    internal sealed class NavMesh3DHierarchy
    {
        public readonly int Count;

        /// <summary>Ключ узла сетки в формате pathfinder'а: (чанк &lt;&lt; 12) | локальный индекс</summary>
        public readonly int[] NodeId;
        public readonly int3[] Abs;
        public readonly int[] Component;
        public readonly int[] Twin;

        // Двери каждой компоненты (CSR по индексу компоненты)
        public readonly int[] CompStart;
        public readonly int[] CompDoors;

        // Стоимости внутри компоненты: для двери i — по всем дверям её компоненты
        // в порядке CompDoors (включая саму себя, 0)
        public readonly int[] IntraStart;
        public readonly float[] IntraCost;

        private NavMesh3DHierarchy(int count, int[] nodeId, int3[] abs, int[] component, int[] twin,
            int[] compStart, int[] compDoors, int[] intraStart, float[] intraCost)
        {
            Count = count;
            NodeId = nodeId;
            Abs = abs;
            Component = component;
            Twin = twin;
            CompStart = compStart;
            CompDoors = compDoors;
            IntraStart = intraStart;
            IntraCost = intraCost;
        }

        public static NavMesh3DHierarchy Build(NavMesh3DChunk[] chunks, int3 chunkCount, NavMesh3DRegions regions)
        {
            int cx = chunkCount.x, cy = chunkCount.y, cz = chunkCount.z;

            // 1. Проёмы между компонентами: все пары узлов на гранях, сгруппированные по паре компонент
            var openings = new Dictionary<long, List<int2>>();

            for (int y = 0; y < cy; y++)
            for (int x = 0; x < cx; x++)
            for (int z = 0; z < cz; z++)
            {
                int a = y * cx * cz + x * cz + z;
                if (chunks[a].IsFullyBlocked) continue;

                if (x + 1 < cx) CollectFace(chunks, regions, openings, a, a + cz, 0);
                if (y + 1 < cy) CollectFace(chunks, regions, openings, a, a + cx * cz, 1);
                if (z + 1 < cz) CollectFace(chunks, regions, openings, a, a + 1, 2);
            }

            // 2. Одна дверь на проём: пара узлов ближе всего к центру проёма
            var nodeId = new List<int>();
            var component = new List<int>();

            foreach (var pair in openings)
            {
                var cells = pair.Value;
                float3 mean = float3.zero;
                foreach (var c in cells) mean += LocalToAbs(c.x);
                mean /= cells.Count;

                int2 best = cells[0];
                float bestDist = float.MaxValue;
                foreach (var c in cells)
                {
                    float d = math.distancesq(LocalToAbs(c.x), mean);
                    if (d < bestDist) { bestDist = d; best = c; }
                }

                nodeId.Add(best.x);
                component.Add((int)(pair.Key >> 32));
                nodeId.Add(best.y);
                component.Add((int)(pair.Key & 0xFFFFFFFF));
            }

            int count = nodeId.Count;
            var ids = nodeId.ToArray();
            var comps = component.ToArray();
            var twin = new int[count];
            var abs = new int3[count];
            for (int i = 0; i < count; i++)
            {
                twin[i] = i ^ 1; // стороны двери идут парами
                abs[i] = IdToAbs(ids[i], chunkCount);
            }

            // 3. Двери по компонентам
            int compTotal = regions.ComponentTotal;
            var compStart = new int[compTotal + 1];
            foreach (int c in comps) compStart[c + 1]++;
            for (int i = 0; i < compTotal; i++) compStart[i + 1] += compStart[i];
            var fill = (int[])compStart.Clone();
            var compDoors = new int[count];
            for (int i = 0; i < count; i++) compDoors[fill[comps[i]]++] = i;

            // 4. Стоимости внутри компонент: от каждой двери поиск по своему чанку
            var intraStart = new int[count + 1];
            for (int i = 0; i < count; i++)
            {
                int c = comps[i];
                intraStart[i + 1] = intraStart[i] + (compStart[c + 1] - compStart[c]);
            }
            var intraCost = new float[intraStart[count]];

            Parallel.For(0, compTotal, () => new NavMesh3DLocalSearch(), (c, _, search) =>
            {
                int from = compStart[c], to = compStart[c + 1];
                if (to - from < 2)
                {
                    if (to - from == 1) intraCost[intraStart[compDoors[from]]] = 0f;
                    return search;
                }

                var chunk = chunks[ids[compDoors[from]] >> 12];
                for (int k = from; k < to; k++)
                {
                    int door = compDoors[k];
                    search.Run(chunk, ids[door] & 4095, -1);
                    for (int m = from; m < to; m++)
                        intraCost[intraStart[door] + (m - from)] = search.Distance(ids[compDoors[m]] & 4095);
                }
                return search;
            }, _ => { });

            return new NavMesh3DHierarchy(count, ids, abs, comps, twin, compStart, compDoors, intraStart, intraCost);
        }

        private static void CollectFace(NavMesh3DChunk[] chunks, NavMesh3DRegions regions,
            Dictionary<long, List<int2>> openings, int a, int b, int axis)
        {
            const int S = NavMesh3DChunk.Size;
            var chunkA = chunks[a];
            var chunkB = chunks[b];
            if (chunkB.IsFullyBlocked) return;

            for (int u = 0; u < S; u++)
            for (int v = 0; v < S; v++)
            {
                int ia, ib;
                switch (axis)
                {
                    case 0: ia = u * S * S + (S - 1) * S + v; ib = u * S * S + v; break;
                    case 1: ia = (S - 1) * S * S + u * S + v; ib = u * S + v; break;
                    default: ia = u * S * S + v * S + (S - 1); ib = u * S * S + v * S; break;
                }

                if (!chunkA.IsWalkable(ia) || !chunkB.IsWalkable(ib)) continue;

                int ca = regions.GetComponent(a, ia);
                int cb = regions.GetComponent(b, ib);
                if (ca < 0 || cb < 0) continue;

                long key = ((long)ca << 32) | (uint)cb;
                if (!openings.TryGetValue(key, out var list))
                    openings[key] = list = new List<int2>();
                list.Add(new int2((a << 12) | ia, (b << 12) | ib));
            }
        }

        /// <summary>Для выбора центра проёма хватает локальных координат: все узлы проёма в одном чанке.</summary>
        private static float3 LocalToAbs(int id)
        {
            int l = id & 4095;
            return new float3((l >> 4) & 15, l >> 8, l & 15);
        }

        private static int3 IdToAbs(int id, int3 chunkCount)
        {
            int chunk = id >> 12, l = id & 4095;
            int layer = chunkCount.x * chunkCount.z;
            int y = chunk / layer;
            int rem = chunk - y * layer;
            int x = rem / chunkCount.z;
            int z = rem - x * chunkCount.z;
            return new int3(x * 16 + ((l >> 4) & 15), y * 16 + (l >> 8), z * 16 + (l & 15));
        }
    }

    /// <summary>
    /// Дейкстра внутри одного чанка (26 направлений, без срезания углов). Не больше 4096 узлов.
    /// Ходы через границу чанка не рассматриваются — путь остаётся внутри компоненты.
    /// </summary>
    internal sealed class NavMesh3DLocalSearch
    {
        private const int N = NavMesh3DChunk.NodeCount;

        private readonly float[] dist = new float[N];
        private readonly short[] parent = new short[N];
        private readonly FastPriorityQueue queue = new FastPriorityQueue(256);

        public int Source { get; private set; } = -1;

        public float Distance(int local) => dist[local];

        /// <summary>Предыдущий узел на пути к источнику, -1 у источника.</summary>
        public int Parent(int local) => parent[local];

        /// <param name="target">Остановиться, когда найден этот узел; -1 — обойти всю компоненту</param>
        public void Run(NavMesh3DChunk chunk, int source, int target)
        {
            Source = source;
            for (int i = 0; i < N; i++) dist[i] = float.PositiveInfinity;
            queue.Clear();
            dist[source] = 0f;
            parent[source] = -1;
            queue.Enqueue(source, 0f);

            var dirs = NavMesh3DPathfinder.Directions;
            var costs = NavMesh3DPathfinder.Costs;
            var required = NavMesh3DPathfinder.RequiredMask;
            Span<int> neighbor = stackalloc int[26];

            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                if (cur == target) return;

                float d = dist[cur];
                int ly = cur >> 8, lx = (cur >> 4) & 15, lz = cur & 15;

                uint free = 0;
                for (int k = 0; k < 26; k++)
                {
                    int nx = lx + dirs[k].x, ny = ly + dirs[k].y, nz = lz + dirs[k].z;
                    if ((uint)nx > 15 || (uint)ny > 15 || (uint)nz > 15) continue;
                    int n = (ny << 8) | (nx << 4) | nz;
                    if (!chunk.IsWalkable(n)) continue;
                    free |= 1u << k;
                    neighbor[k] = n;
                }

                for (int k = 0; k < 26; k++)
                {
                    if ((free & (1u << k)) == 0 || (free & required[k]) != required[k]) continue;
                    int n = neighbor[k];
                    float nd = d + costs[k];
                    if (nd < dist[n])
                    {
                        dist[n] = nd;
                        parent[n] = (short)cur;
                        queue.Enqueue(n, nd);
                    }
                }
            }
        }
    }
}
