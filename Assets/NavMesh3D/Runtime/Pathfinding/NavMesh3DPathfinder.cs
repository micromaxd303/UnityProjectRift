using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace NavMesh3DSystem
{
    /// <summary>
    /// Результат поиска пути.
    /// </summary>
    public enum PathResult
    {
        /// <summary>Путь найден</summary>
        Found,
        
        /// <summary>Путь не существует (разные регионы)</summary>
        NoPath,
        
        /// <summary>Старт или цель вне навмеша</summary>
        InvalidEndpoints,
        
        /// <summary>Старт или цель заблокированы</summary>
        BlockedEndpoints,
        
        /// <summary>Превышен лимит итераций (путь может существовать, но слишком сложный)</summary>
        IterationLimit,
        
        /// <summary>NavMesh не запечён</summary>
        NotBaked
    }
    
    /// <summary>
    /// Запрос на поиск пути.
    /// </summary>
    public struct PathRequest
    {
        public Vector3 start;
        public Vector3 end;
        
        /// <summary>
        /// Максимальное количество итераций A*.
        /// 0 = без лимита (опасно!)
        /// Рекомендуется: 10000-50000
        /// </summary>
        public int maxIterations;
        
        /// <summary>
        /// Разрешить диагональное движение (26 направлений вместо 6).
        /// Путь будет короче, но поиск медленнее.
        /// </summary>
        public bool allowDiagonal;
        
        /// <summary>
        /// Не сглаживать путь (вернуть сырую цепочку узлов сетки).
        /// По умолчанию false — путь спрямляется по прямой видимости.
        /// </summary>
        public bool skipSmoothing;
        
        /// <summary>
        /// Вес эвристики A*. 0 — по умолчанию (NavMesh3DPathfinder.DefaultHeuristicWeight).
        /// 1 — кратчайший путь, но в больших залах A* перебирает огромный «пузырь» узлов.
        /// 1.2–1.5 — в разы меньше итераций ценой чуть менее коротких путей.
        /// </summary>
        public float heuristicWeight;
        
        public static PathRequest Create(Vector3 start, Vector3 end)
        {
            return new PathRequest
            {
                start = start,
                end = end,
                maxIterations = 20000,
                allowDiagonal = true
            };
        }
    }
    
    /// <summary>
    /// Ответ на запрос пути.
    /// </summary>
    public class PathResponse
    {
        public PathResult result;
        public List<Vector3> path;
        public int iterations;
        public float pathLength;
        
        /// <summary>Путь успешно найден</summary>
        public bool Success => result == PathResult.Found;
    }
    
    /// <summary>
    /// Поиск пути в 3D навигационной сетке.
    /// 
    /// АЛГОРИТМ:
    /// 1. Переводим старт и цель в узлы. Если точка в непроходимом узле (агент прижат
    ///    к стене), берём ближайший проходимый в радиусе SnapRadius узлов.
    /// 2. Проверяем регионы — если разные, пути нет (O(1), связность точная по узлам)
    /// 3. A* с лимитом итераций
    /// 4. Спрямляем путь по прямой видимости в сетке (без физики)
    /// 
    /// ДАННЫЕ ПОИСКА:
    /// Вместо Dictionary/HashSet на каждый узел — страницы массивов по чанкам.
    /// Страница создаётся при первом касании чанка и переиспользуется между поисками;
    /// «очистка» — смена номера поиска, массив чистится только при первом касании.
    /// Ключ узла: (индекс чанка &lt;&lt; 12) | локальный индекс — без деления и хеширования.
    /// 
    /// ДИАГОНАЛИ:
    /// Диагональный шаг разрешён, только если свободны все осевые (и рёберные для углов)
    /// соседи, через которые он проходит. Иначе путь протискивается между двумя
    /// заблокированными узлами и агент цепляет стену.
    /// </summary>
    public class NavMesh3DPathfinder
    {
        /// <summary>На сколько узлов можно сдвинуть старт/цель из непроходимого узла.</summary>
        public const int SnapRadius = 4;
        
        /// <summary>
        /// Для цели радиус больше: цель часто стоит на полу (ноги игрока), а проходимые
        /// узлы начинаются на радиус агента выше.
        /// </summary>
        public const int EndSnapRadius = 6;
        
        /// <summary>Вес эвристики, если в запросе 0.</summary>
        public const float DefaultHeuristicWeight = 1.3f;
        
        /// <summary>
        /// Насколько оценка по графу компонент может завышать путь: узел и цель лежат
        /// не в центрах своих компонент. В узлах (один чанк).
        /// </summary>
        private const float AbstractSlack = Size;
        
        private const int Size = NavMesh3DSettings.ChunkSize;
        private const int NodesPerChunk = NavMesh3DChunk.NodeCount;
        
        private readonly NavMesh3D navMesh;
        private readonly NavMesh3DChunk[] chunks;
        private readonly int3 chunkCount;
        private readonly int3 nodeLimit;
        private readonly int chunkLayer; // cx * cz
        
        private readonly SearchPage[] pages;
        private int searchStamp;
        
        private readonly FastPriorityQueue openSet = new FastPriorityQueue(1024);
        private readonly List<int> nodeBuffer = new List<int>(256);
        private readonly List<int> smoothBuffer = new List<int>(64);
        
        private sealed class SearchPage
        {
            public int stamp = -1;
            public readonly float[] g = new float[NodesPerChunk];
            public readonly int[] parent = new int[NodesPerChunk];
            public readonly ulong[] closed = new ulong[NodesPerChunk / 64];
        }
        
        // ===== Направления =====
        
        internal static readonly int3[] Directions;
        internal static readonly float[] Costs;
        internal static readonly uint[] RequiredMask;
        private static readonly int[] AxisDirections;
        private static readonly int[] AllDirections;
        
        static NavMesh3DPathfinder()
        {
            var dirs = new List<int3>();
            for (int x = -1; x <= 1; x++)
            for (int y = -1; y <= 1; y++)
            for (int z = -1; z <= 1; z++)
                if (x != 0 || y != 0 || z != 0)
                    dirs.Add(new int3(x, y, z));
            
            Directions = dirs.ToArray();
            Costs = new float[Directions.Length];
            RequiredMask = new uint[Directions.Length];
            var axis = new List<int>();
            var all = new List<int>();
            
            for (int i = 0; i < Directions.Length; i++)
            {
                int3 d = Directions[i];
                int nonZero = math.csum(math.abs(d));
                Costs[i] = math.sqrt(nonZero);
                all.Add(i);
                if (nonZero == 1) axis.Add(i);
                
                // Нужны свободными все «под-шаги»: направления, чьи ненулевые компоненты
                // совпадают с компонентами d (для ребра — 2 оси, для угла — 3 оси и 3 ребра)
                for (int j = 0; j < Directions.Length; j++)
                {
                    if (j == i) continue;
                    int3 e = Directions[j];
                    if (math.all(e == 0 | e == d))
                        RequiredMask[i] |= 1u << j;
                }
            }
            
            AxisDirections = axis.ToArray();
            AllDirections = all.ToArray();
        }
        
        public NavMesh3DPathfinder(NavMesh3D navMesh)
        {
            this.navMesh = navMesh;
            chunks = navMesh.FlatChunks;
            chunkCount = navMesh.ChunkCount;
            nodeLimit = chunkCount * Size;
            chunkLayer = chunkCount.x * chunkCount.z;
            pages = new SearchPage[chunks.Length];
        }
        
        /// <summary>Накопленная статистика поиска для замеров (сбрасывается через ResetStats).</summary>
        public struct PathStats
        {
            public int paths;
            public int found;
            public int noPath;
            public int iterationLimit;
            public int blockedEndpoints;
            public int invalidEndpoints;
            public int otherFailures;
            public long totalIterations;
            public int maxIterations;
            public double totalMs;
            public double maxMs;
            /// <summary>Самый тяжёлый кадр: сумма всех поисков за кадр, мс</summary>
            public double maxFrameMs;
            public int maxPathsPerFrame;
        }
        
        public PathStats Stats => stats;
        
        private PathStats stats;
        private int statsFrame = -1;
        private double frameMs;
        private int framePaths;
        private readonly System.Diagnostics.Stopwatch stopwatch = new System.Diagnostics.Stopwatch();
        
        public void ResetStats() => stats = default;
        
        /// <summary>
        /// Ищет путь между двумя точками.
        /// </summary>
        public PathResponse FindPath(PathRequest request)
        {
            stopwatch.Restart();
            var response = FindPathInternal(request);
            stopwatch.Stop();
            
            double ms = stopwatch.Elapsed.TotalMilliseconds;
            int frame = Time.frameCount;
            if (frame != statsFrame)
            {
                statsFrame = frame;
                frameMs = 0;
                framePaths = 0;
            }
            frameMs += ms;
            framePaths++;
            
            stats.paths++;
            stats.totalMs += ms;
            stats.maxMs = Math.Max(stats.maxMs, ms);
            stats.maxFrameMs = Math.Max(stats.maxFrameMs, frameMs);
            stats.maxPathsPerFrame = Math.Max(stats.maxPathsPerFrame, framePaths);
            stats.totalIterations += response.iterations;
            stats.maxIterations = Math.Max(stats.maxIterations, response.iterations);
            switch (response.result)
            {
                case PathResult.Found: stats.found++; break;
                case PathResult.NoPath: stats.noPath++; break;
                case PathResult.IterationLimit: stats.iterationLimit++; break;
                case PathResult.BlockedEndpoints: stats.blockedEndpoints++; break;
                case PathResult.InvalidEndpoints: stats.invalidEndpoints++; break;
                default: stats.otherFailures++; break;
            }
            
            return response;
        }
        
        private PathResponse FindPathInternal(PathRequest request)
        {
            var response = new PathResponse
            {
                path = new List<Vector3>(),
                iterations = 0
            };
            
            if (!navMesh.IsBaked)
            {
                response.result = PathResult.NotBaked;
                return response;
            }
            
            var startNode = navMesh.WorldToNode(request.start);
            var endNode = navMesh.WorldToNode(request.end);
            
            int3 startAbs = startNode.ToAbsolute();
            int3 endAbs = endNode.ToAbsolute();
            
            // Далеко за пределами сетки — сразу отказ; у самой границы ещё можно притянуть
            if (!InSnapRange(startAbs, SnapRadius) || !InSnapRange(endAbs, EndSnapRadius))
            {
                response.result = PathResult.InvalidEndpoints;
                return response;
            }
            
            bool endSnapped = !IsWalkable(endAbs);
            if (!IsWalkable(startAbs) && !navMesh.TryFindNearestWalkableNode(startNode, SnapRadius, out startNode) ||
                endSnapped && !navMesh.TryFindNearestWalkableNode(endNode, EndSnapRadius, out endNode))
            {
                response.result = PathResult.BlockedEndpoints;
                return response;
            }
            
            startAbs = startNode.ToAbsolute();
            endAbs = endNode.ToAbsolute();
            Vector3 exactEnd = endSnapped ? navMesh.NodeToWorld(endNode) : request.end;
            
            // Регионы: точная связность, O(1)
            if (!navMesh.AreConnected(startNode, endNode))
            {
                response.result = PathResult.NoPath;
                return response;
            }
            
            int startId = NodeId(startAbs);
            int endId = NodeId(endAbs);
            
            if (startId == endId)
            {
                response.result = PathResult.Found;
                response.path.Add(request.start);
                response.path.Add(exactEnd);
                response.pathLength = Vector3.Distance(request.start, exactEnd);
                return response;
            }
            
            float weight = request.heuristicWeight > 0f ? request.heuristicWeight : DefaultHeuristicWeight;
            
            // HPA*: по графу дверей, если он есть и узлы не менялись после запекания
            int hpa = request.allowDiagonal
                ? HierarchicalPath(startId, endId, endAbs, weight, request.maxIterations, nodeBuffer, out response.iterations)
                : -1;
            
            if (hpa == 0)
            {
                response.result = request.maxIterations > 0 && response.iterations >= request.maxIterations
                    ? PathResult.IterationLimit
                    : PathResult.NoPath;
                return response;
            }
            
            if (hpa < 0)
            {
                // Обычный A* по всей сетке
                PrepareAbstractHeuristic(endId);
                var directions = request.allowDiagonal ? AllDirections : AxisDirections;
                
                if (!AStar(startId, startAbs, endId, endAbs, directions, weight, request.maxIterations, out response.iterations))
                {
                    response.result = request.maxIterations > 0 && response.iterations >= request.maxIterations
                        ? PathResult.IterationLimit
                        : PathResult.NoPath;
                    return response;
                }
                
                ReconstructPath(startId, endId, nodeBuffer);
            }
            
            var nodes = nodeBuffer;
            if (!request.skipSmoothing)
            {
                Smooth(nodeBuffer, smoothBuffer);
                nodes = smoothBuffer;
            }
            
            // Точные старт и конец вместо крайних узлов
            response.path.Capacity = nodes.Count;
            response.path.Add(request.start);
            for (int i = 1; i < nodes.Count - 1; i++)
                response.path.Add(navMesh.NodeToWorld(GlobalNodeCoord.FromAbsolute(IdToAbsolute(nodes[i]))));
            response.path.Add(exactEnd);
            
            response.pathLength = CalculatePathLength(response.path);
            response.result = PathResult.Found;
            return response;
        }
        
        /// <summary>
        /// Проверяет прямую видимость между двумя мировыми точками по сетке (без физики).
        /// Полезно агентам, чтобы лететь напрямую, когда цель видна.
        /// </summary>
        public bool HasLineOfSight(Vector3 from, Vector3 to)
        {
            if (!navMesh.IsBaked) return false;
            int3 a = navMesh.WorldToNode(from).ToAbsolute();
            int3 b = navMesh.WorldToNode(to).ToAbsolute();
            return InGrid(a) && InGrid(b) && IsWalkable(a) && LineWalkable(a, b);
        }
        
        // ===== A* =====
        
        private bool AStar(int startId, int3 startAbs, int endId, int3 endAbs, int[] directions,
            float weight, int maxIterations, out int iterations)
        {
            iterations = 0;
            searchStamp++;
            openSet.Clear();
            
            var startPage = Touch(startId >> 12);
            startPage.g[startId & 4095] = 0f;
            startPage.parent[startId & 4095] = -1;
            openSet.Enqueue(startId, weight * GuidedHeuristic(startId, startAbs, endAbs));
            
            Span<int> neighborIds = stackalloc int[26];
            
            while (openSet.Count > 0)
            {
                iterations++;
                if (maxIterations > 0 && iterations >= maxIterations)
                    return false;
                
                int current = openSet.Dequeue();
                if (current == endId)
                    return true;
                
                var page = Touch(current >> 12);
                int local = current & 4095;
                ulong bit = 1UL << (local & 63);
                if ((page.closed[local >> 6] & bit) != 0)
                    continue; // устаревший дубликат в очереди
                page.closed[local >> 6] |= bit;
                
                float g = page.g[local];
                int3 abs = IdToAbsolute(current);
                
                // Сначала узнаём, какие соседи свободны, — нужно для запрета срезания углов
                uint free = 0;
                for (int k = 0; k < directions.Length; k++)
                {
                    int d = directions[k];
                    int3 n = abs + Directions[d];
                    if (!InGrid(n)) continue;
                    int id = NodeId(n);
                    if (chunks[id >> 12].IsWalkable(id & 4095))
                    {
                        free |= 1u << d;
                        neighborIds[d] = id;
                    }
                }
                
                for (int k = 0; k < directions.Length; k++)
                {
                    int d = directions[k];
                    if ((free & (1u << d)) == 0 || (free & RequiredMask[d]) != RequiredMask[d])
                        continue;
                    
                    int nid = neighborIds[d];
                    var npage = Touch(nid >> 12);
                    int nl = nid & 4095;
                    if ((npage.closed[nl >> 6] & (1UL << (nl & 63))) != 0)
                        continue;
                    
                    float tentative = g + Costs[d];
                    if (tentative < npage.g[nl])
                    {
                        npage.g[nl] = tentative;
                        npage.parent[nl] = current;
                        openSet.Enqueue(nid, tentative + weight * GuidedHeuristic(nid, abs + Directions[d], endAbs));
                    }
                }
            }
            
            return false;
        }
        
        private SearchPage Touch(int chunkIndex)
        {
            var page = pages[chunkIndex];
            if (page == null)
                pages[chunkIndex] = page = new SearchPage();
            
            if (page.stamp != searchStamp)
            {
                Array.Fill(page.g, float.PositiveInfinity);
                Array.Clear(page.closed, 0, page.closed.Length);
                page.stamp = searchStamp;
            }
            
            return page;
        }
        
        // ===== HPA* =====
        
        private readonly NavMesh3DLocalSearch startSearch = new NavMesh3DLocalSearch();
        private readonly NavMesh3DLocalSearch goalSearch = new NavMesh3DLocalSearch();
        private NavMesh3DChunk goalSearchChunk;
        private int goalSearchGrid;
        private readonly NavMesh3DLocalSearch refineSearch = new NavMesh3DLocalSearch();
        private readonly FastPriorityQueue doorQueue = new FastPriorityQueue(256);
        private readonly List<int> doorPath = new List<int>(64);
        private readonly List<int> segment = new List<int>(64);
        private float[] doorG;
        private int[] doorParent;
        private int[] doorStamp;
        private int doorSearch;
        
        /// <returns>1 — путь в result, 0 — пути нет, -1 — HPA* неприменим (нужен обычный A*)</returns>
        private int HierarchicalPath(int startId, int endId, int3 endAbs, float weight, int maxIterations,
            List<int> result, out int iterations)
        {
            iterations = 0;
            var h = navMesh.Hierarchy;
            var regions = navMesh.Regions;
            if (h == null)
                return -1;
            
            int cs = regions.GetComponent(startId >> 12, startId & 4095);
            int cg = regions.GetComponent(endId >> 12, endId & 4095);
            if (cs < 0 || cg < 0)
                return -1; // carving поменял узлы — граф не знает о них
            
            var startChunk = chunks[startId >> 12];
            int startBase = startId & ~4095;
            
            // Старт и цель в одной компоненте — путь внутри одного чанка
            if (cs == cg)
            {
                startSearch.Run(startChunk, startId & 4095, endId & 4095);
                if (float.IsInfinity(startSearch.Distance(endId & 4095)))
                    return -1;
                
                result.Clear();
                AppendToSource(startSearch, endId & 4095, startBase, result);
                result.Reverse();
                return 1;
            }
            
            startSearch.Run(startChunk, startId & 4095, -1);
            
            // Все враги обычно летят к одной цели (игрок) — поиск от цели переиспользуем
            var goalChunk = chunks[endId >> 12];
            if (goalSearchChunk != goalChunk || goalSearch.Source != (endId & 4095) || goalSearchGrid != navMesh.GridVersion)
            {
                goalSearch.Run(goalChunk, endId & 4095, -1);
                goalSearchChunk = goalChunk;
                goalSearchGrid = navMesh.GridVersion;
            }
            
            // A* по дверям. Индексы Count и Count + 1 — виртуальные старт и цель
            int count = h.Count;
            int goal = count + 1;
            if (doorG == null || doorG.Length != count + 2)
            {
                doorG = new float[count + 2];
                doorParent = new int[count + 2];
                doorStamp = new int[count + 2];
                doorSearch = 0;
            }
            doorSearch++;
            doorQueue.Clear();
            
            // Двери компоненты старта
            for (int k = h.CompStart[cs]; k < h.CompStart[cs + 1]; k++)
            {
                int door = h.CompDoors[k];
                float g = startSearch.Distance(h.NodeId[door] & 4095);
                if (float.IsInfinity(g)) continue;
                Relax(door, g, count, weight, h, endAbs);
            }
            
            while (doorQueue.Count > 0)
            {
                iterations++;
                if (maxIterations > 0 && iterations >= maxIterations)
                    return 0;
                
                int cur = doorQueue.Dequeue();
                if (cur == goal)
                    break;
                
                float g = doorG[cur];
                
                // Дверь в компоненте цели — можно войти в цель
                if (h.Component[cur] == cg)
                {
                    float toGoal = goalSearch.Distance(h.NodeId[cur] & 4095);
                    if (!float.IsInfinity(toGoal))
                        RelaxGoal(goal, g + toGoal, cur);
                }
                
                // Через проём на другую сторону
                Relax(h.Twin[cur], g + 1f, cur, weight, h, endAbs);
                
                // К другим дверям своей компоненты
                int comp = h.Component[cur];
                int from = h.CompStart[comp];
                int costs = h.IntraStart[cur];
                for (int k = from; k < h.CompStart[comp + 1]; k++)
                {
                    int other = h.CompDoors[k];
                    if (other == cur) continue;
                    float c = h.IntraCost[costs + (k - from)];
                    if (float.IsInfinity(c)) continue;
                    Relax(other, g + c, cur, weight, h, endAbs);
                }
            }
            
            if (doorStamp[goal] != doorSearch)
                return 0;
            
            // Цепочка дверей от цели к старту
            doorPath.Clear();
            for (int d = doorParent[goal]; d != count; d = doorParent[d])
                doorPath.Add(d);
            doorPath.Reverse();
            
            // Уточнение по сетке
            result.Clear();
            int first = doorPath[0];
            AppendToSource(startSearch, h.NodeId[first] & 4095, startBase, result);
            result.Reverse(); // старт ... первая дверь
            
            for (int i = 1; i < doorPath.Count; i++)
            {
                int a = doorPath[i - 1], b = doorPath[i];
                if (h.Twin[a] == b || LineWalkable(h.Abs[a], h.Abs[b]))
                {
                    result.Add(h.NodeId[b]);
                    continue;
                }
                
                // Внутри компоненты стен не видно напрямую — короткий поиск в пределах чанка
                int baseId = h.NodeId[a] & ~4095;
                refineSearch.Run(chunks[h.NodeId[a] >> 12], h.NodeId[a] & 4095, h.NodeId[b] & 4095);
                if (float.IsInfinity(refineSearch.Distance(h.NodeId[b] & 4095)))
                    return -1; // препятствие перекрыло участок после запекания — обычный A* по текущей сетке
                segment.Clear();
                AppendToSource(refineSearch, h.NodeId[b] & 4095, baseId, segment);
                for (int k = segment.Count - 2; k >= 0; k--) // без первой (это a)
                    result.Add(segment[k]);
            }
            
            // Последняя дверь ... цель: родители goalSearch ведут к цели
            int last = doorPath[doorPath.Count - 1];
            int endBase = endId & ~4095;
            segment.Clear();
            AppendToSource(goalSearch, h.NodeId[last] & 4095, endBase, segment);
            for (int k = 1; k < segment.Count; k++)
                result.Add(segment[k]);
            
            return 1;
        }
        
        private void Relax(int door, float g, int parent, float weight, NavMesh3DHierarchy h, int3 endAbs)
        {
            if (doorStamp[door] == doorSearch && doorG[door] <= g)
                return;
            
            // Дверь закрыта препятствием
            int id = h.NodeId[door];
            if (!chunks[id >> 12].IsWalkable(id & 4095))
                return;
            doorStamp[door] = doorSearch;
            doorG[door] = g;
            doorParent[door] = parent;
            doorQueue.Enqueue(door, g + weight * Heuristic(h.Abs[door], endAbs));
        }
        
        private void RelaxGoal(int goal, float g, int parent)
        {
            if (doorStamp[goal] == doorSearch && doorG[goal] <= g)
                return;
            doorStamp[goal] = doorSearch;
            doorG[goal] = g;
            doorParent[goal] = parent;
            doorQueue.Enqueue(goal, g);
        }
        
        /// <summary>Добавляет путь от local к источнику поиска (local первым), ключами pathfinder'а.</summary>
        private static void AppendToSource(NavMesh3DLocalSearch search, int local, int baseId, List<int> into)
        {
            for (int l = local; l >= 0; l = search.Parent(l))
                into.Add(baseId | l);
        }
        
        // ===== Эвристика по графу компонент =====
        //
        // Октильное расстояние «не видит» стен: в пещере A* лезет в каждый тупиковый зал,
        // который лежит в сторону цели, и выжигает лимит итераций. Поэтому перед поиском
        // считаем Дейкстрой по графу компонент (сотни-тысячи вершин, доли мс) расстояние
        // от каждой компоненты до цели по реальным проходам. Узлу в тупике оценка сразу
        // большая, и A* туда не идёт.
        
        private float[] componentDistance;
        private readonly FastPriorityQueue componentQueue = new FastPriorityQueue(256);
        private int goalComponent = -1;
        
        private void PrepareAbstractHeuristic(int endId)
        {
            var regions = navMesh.Regions;
            goalComponent = regions.GetComponent(endId >> 12, endId & 4095);
            if (goalComponent < 0)
                return; // сетка менялась после запекания — обойдёмся октильной оценкой
            
            if (componentDistance == null || componentDistance.Length != regions.ComponentTotal)
                componentDistance = new float[regions.ComponentTotal];
            
            regions.ComputeDistancesTo(goalComponent, componentDistance, componentQueue);
        }
        
        private float GuidedHeuristic(int id, int3 p, int3 goal)
        {
            float h = Heuristic(p, goal);
            if (goalComponent < 0)
                return h;
            
            int comp = navMesh.Regions.GetComponent(id >> 12, id & 4095);
            if (comp < 0 || comp == goalComponent)
                return h;
            
            float graph = componentDistance[comp] - AbstractSlack;
            return graph > h && !float.IsInfinity(graph) ? graph : h;
        }
        
        /// <summary>
        /// Октильное расстояние в 3D: допустимая эвристика для 26 направлений.
        /// </summary>
        private static float Heuristic(int3 a, int3 b)
        {
            int3 d = math.abs(a - b);
            int min = math.cmin(d);
            int max = math.cmax(d);
            int mid = d.x + d.y + d.z - min - max;
            
            const float sqrt2 = 1.41421356f;
            const float sqrt3 = 1.73205081f;
            
            return max + (sqrt2 - 1f) * mid + (sqrt3 - sqrt2) * min;
        }
        
        private void ReconstructPath(int startId, int endId, List<int> result)
        {
            result.Clear();
            int current = endId;
            while (current != startId)
            {
                result.Add(current);
                current = pages[current >> 12].parent[current & 4095];
            }
            result.Add(startId);
            result.Reverse();
        }
        
        // ===== Сглаживание =====
        
        /// <summary>
        /// Жадное спрямление: от опорного узла тянемся к самому дальнему видимому.
        /// </summary>
        private void Smooth(List<int> nodes, List<int> result)
        {
            result.Clear();
            result.Add(nodes[0]);
            
            int3 anchor = IdToAbsolute(nodes[0]);
            for (int i = 2; i < nodes.Count; i++)
            {
                if (LineWalkable(anchor, IdToAbsolute(nodes[i])))
                    continue;
                
                result.Add(nodes[i - 1]);
                anchor = IdToAbsolute(nodes[i - 1]);
            }
            
            result.Add(nodes[nodes.Count - 1]);
        }
        
        /// <summary>
        /// Обход вокселей по отрезку между центрами узлов (Amanatides–Woo).
        /// Когда отрезок проходит ровно через ребро или угол, проверяются все соседние
        /// узлы вокруг него — чтобы не проскочить между двумя заблокированными.
        /// </summary>
        private bool LineWalkable(int3 a, int3 b)
        {
            int3 d = b - a;
            int3 step = math.sign(d);
            float3 ad = math.abs((float3)d);
            float3 tDelta = 1f / ad; // по оси без движения — бесконечность
            float3 tMax = tDelta * 0.5f;
            int3 cur = a;
            int budget = math.csum(math.abs(d)); // ровно столько осевых шагов до b
            
            while (math.any(cur != b))
            {
                float t = math.cmin(tMax);
                bool3 hit = tMax <= t + 1e-6f;
                int hitCount = math.csum(math.select(int3.zero, 1, hit));
                
                budget -= hitCount;
                if (budget < 0)
                    return false; // ошибка округления увела мимо b — считаем, что не видно
                
                if (hitCount > 1)
                {
                    // Пересечение ребра/угла: все промежуточные комбинации
                    for (int mask = 1; mask < 7; mask++)
                    {
                        int3 sub = new int3(mask & 1, (mask >> 1) & 1, (mask >> 2) & 1);
                        if (math.any(sub == 1 & !hit)) continue;
                        if (math.all((sub == 1) == hit)) continue; // это сам следующий узел
                        int3 p = cur + sub * step;
                        if (!InGrid(p) || !IsWalkable(p)) return false;
                    }
                }
                
                cur += math.select(int3.zero, step, hit);
                tMax += math.select(float3.zero, tDelta, hit);
                
                if (!InGrid(cur) || !IsWalkable(cur))
                    return false;
            }
            
            return true;
        }
        
        // ===== Вспомогательные методы =====
        
        private bool InGrid(int3 p) => math.all(p >= 0) && math.all(p < nodeLimit);
        
        private bool InSnapRange(int3 p, int radius) => math.all(p >= -radius) && math.all(p < nodeLimit + radius);
        
        private bool IsWalkable(int3 p)
        {
            if (!InGrid(p)) return false;
            int id = NodeId(p);
            return chunks[id >> 12].IsWalkable(id & 4095);
        }
        
        /// <summary>Ключ узла: индекс чанка (y * cx * cz + x * cz + z) &lt;&lt; 12 | локальный индекс (y * 256 + x * 16 + z).</summary>
        private int NodeId(int3 p)
        {
            int3 c = p >> 4;
            int chunk = c.y * chunkLayer + c.x * chunkCount.z + c.z;
            return (chunk << 12) | ((p.y & 15) << 8) | ((p.x & 15) << 4) | (p.z & 15);
        }
        
        private int3 IdToAbsolute(int id)
        {
            int chunk = id >> 12;
            int local = id & 4095;
            int cy = chunk / chunkLayer;
            int rem = chunk - cy * chunkLayer;
            int cx = rem / chunkCount.z;
            int cz = rem - cx * chunkCount.z;
            return new int3(
                cx * Size + ((local >> 4) & 15),
                cy * Size + (local >> 8),
                cz * Size + (local & 15));
        }
        
        private static float CalculatePathLength(List<Vector3> path)
        {
            float length = 0;
            for (int i = 1; i < path.Count; i++)
                length += Vector3.Distance(path[i - 1], path[i]);
            return length;
        }
    }
}
