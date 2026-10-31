using System.Collections.Generic;
using Unity.Mathematics;

namespace NavMesh3DSystem
{
    /// <summary>
    /// Менеджер регионов — определяет какие чанки связаны между собой.
    /// 
    /// ЗАЧЕМ ЭТО НУЖНО:
    /// Представь сетку 500³ узлов. Игрок в комнате A, враг в комнате B.
    /// Между комнатами стена без проходов.
    /// 
    /// Без регионов: A* обойдёт МИЛЛИОНЫ узлов прежде чем сдастся.
    /// С регионами: проверка за O(1) — "разные регионы = пути нет".
    /// 
    /// АЛГОРИТМ UNION-FIND:
    /// Это структура данных для отслеживания связных компонент.
    /// Две операции:
    /// - Find(x): найти "корень" региона для элемента x
    /// - Union(x, y): объединить регионы x и y
    /// 
    /// Оптимизации:
    /// - Path Compression: при Find() сразу указываем на корень
    /// - Union by Rank: при Union() меньший регион присоединяем к большему
    /// 
    /// Это даёт амортизированную сложность O(α(n)) ≈ O(1).
    /// </summary>
    public class NavMesh3DRegionManager
    {
        /// <summary>
        /// Родитель каждого чанка в Union-Find структуре.
        /// parent[i] = i означает что i — корень своего региона.
        /// </summary>
        private int[] parent;
        
        /// <summary>
        /// Ранг (примерная глубина) дерева для каждого корня.
        /// Используется для оптимизации Union.
        /// </summary>
        private int[] rank;
        
        /// <summary>
        /// Размер каждого региона (количество чанков).
        /// </summary>
        private int[] regionSize;
        
        /// <summary>
        /// Общее количество чанков.
        /// </summary>
        private int count;
        
        /// <summary>
        /// Текущее количество отдельных регионов.
        /// Уменьшается при каждом Union.
        /// </summary>
        public int RegionCount { get; private set; }
        
        /// <summary>
        /// Создаёт менеджер регионов для заданного количества чанков.
        /// </summary>
        public NavMesh3DRegionManager(int chunkCount)
        {
            count = chunkCount;
            parent = new int[count];
            rank = new int[count];
            regionSize = new int[count];
            
            // Изначально каждый чанк — отдельный регион
            for (int i = 0; i < count; i++)
            {
                parent[i] = i;      // Каждый сам себе корень
                rank[i] = 0;        // Начальный ранг = 0
                regionSize[i] = 1;  // Размер = 1 чанк
            }
            
            RegionCount = count;
        }
        
        /// <summary>
        /// Находит корневой ID региона для чанка.
        /// Использует Path Compression для ускорения последующих запросов.
        /// 
        /// КАК РАБОТАЕТ PATH COMPRESSION:
        /// До: 0 → 1 → 2 → 3 (корень)
        /// После Find(0): 0 → 3, 1 → 3, 2 → 3
        /// 
        /// Все промежуточные узлы теперь указывают прямо на корень.
        /// </summary>
        public int Find(int chunkIndex)
        {
            if (chunkIndex < 0 || chunkIndex >= count)
                return -1;
            
            // Рекурсивно находим корень и сжимаем путь
            if (parent[chunkIndex] != chunkIndex)
            {
                parent[chunkIndex] = Find(parent[chunkIndex]);
            }
            
            return parent[chunkIndex];
        }
        
        /// <summary>
        /// Объединяет регионы двух чанков.
        /// Использует Union by Rank для балансировки дерева.
        /// 
        /// КАК РАБОТАЕТ UNION BY RANK:
        /// Меньшее дерево присоединяется к большему.
        /// Это предотвращает создание длинных цепочек.
        /// </summary>
        /// <returns>true если регионы были разные и объединены</returns>
        public bool Union(int chunkA, int chunkB)
        {
            int rootA = Find(chunkA);
            int rootB = Find(chunkB);
            
            // Уже в одном регионе
            if (rootA == rootB)
                return false;
            
            // Присоединяем меньший ранг к большему
            if (rank[rootA] < rank[rootB])
            {
                parent[rootA] = rootB;
                regionSize[rootB] += regionSize[rootA];
            }
            else if (rank[rootA] > rank[rootB])
            {
                parent[rootB] = rootA;
                regionSize[rootA] += regionSize[rootB];
            }
            else
            {
                // Ранги равны — выбираем произвольно и увеличиваем ранг
                parent[rootB] = rootA;
                regionSize[rootA] += regionSize[rootB];
                rank[rootA]++;
            }
            
            RegionCount--;
            return true;
        }
        
        /// <summary>
        /// Проверяет, находятся ли два чанка в одном регионе.
        /// Это главный метод для быстрой проверки достижимости!
        /// </summary>
        public bool AreConnected(int chunkA, int chunkB)
        {
            return Find(chunkA) == Find(chunkB);
        }
        
        /// <summary>
        /// Возвращает размер региона, содержащего указанный чанк.
        /// </summary>
        public int GetRegionSize(int chunkIndex)
        {
            int root = Find(chunkIndex);
            return root >= 0 ? regionSize[root] : 0;
        }
        
        /// <summary>
        /// Возвращает все корни регионов (уникальные ID регионов).
        /// Полезно для отладки и визуализации.
        /// </summary>
        public List<int> GetAllRegionRoots()
        {
            var roots = new HashSet<int>();
            
            for (int i = 0; i < count; i++)
            {
                roots.Add(Find(i));
            }
            
            return new List<int>(roots);
        }
        
        /// <summary>
        /// Сбрасывает все регионы (каждый чанк снова отдельный регион).
        /// Вызывается при пересоздании навмеша.
        /// </summary>
        public void Reset()
        {
            for (int i = 0; i < count; i++)
            {
                parent[i] = i;
                rank[i] = 0;
                regionSize[i] = 1;
            }
            RegionCount = count;
        }
        
        /// <summary>
        /// Помечает чанк как изолированный (отдельный регион).
        /// Используется для полностью заблокированных чанков.
        /// </summary>
        public void MarkIsolated(int chunkIndex)
        {
            // Просто делаем чанк корнем самого себя
            // При следующем Union он будет правильно обработан
            parent[chunkIndex] = chunkIndex;
            rank[chunkIndex] = 0;
            regionSize[chunkIndex] = 1;
        }
    }
    
    /// <summary>
    /// Точная связность на уровне узлов.
    /// 
    /// ПОЧЕМУ НЕ ПО ЧАНКАМ:
    /// Раньше два соседних чанка объединялись, если у обоих есть проходимые узлы
    /// на общей грани — даже если эти узлы не совпадают, а внутри чанка воздух
    /// разделён стеной. В пещере так склеиваются соседние туннели, IsReachable
    /// говорит «да», и A* выжигает весь лимит итераций, прежде чем сдаться.
    /// 
    /// АЛГОРИТМ:
    /// 1. Внутри каждого чанка flood fill (6 соседей) делит узлы на компоненты.
    ///    Метки храним только у чанков, где компонент больше одной.
    /// 2. Компоненты всех чанков — элементы Union-Find.
    /// 3. На каждой общей грани объединяем компоненты узлов, проходимых с обеих сторон.
    /// 
    /// A* не срезает углы, поэтому 6-связность совпадает с тем, куда он реально может пройти.
    /// </summary>
    public class NavMesh3DRegions
    {
        private const ushort NoLabel = ushort.MaxValue;
        private const int Size = NavMesh3DChunk.Size;
        
        private readonly int[] componentBase;
        private readonly int[] componentCount;
        private readonly ushort[][] labels;
        private readonly NavMesh3DRegionManager unionFind;
        
        // Граф компонент (для эвристики A*): центр каждой компоненты в координатах узлов
        // и рёбра между соседними компонентами в формате CSR
        private readonly float3[] centroids;
        private readonly int[] adjStart;
        private readonly int[] adjList;
        private readonly float[] adjCost;
        
        /// <summary>Количество связных областей проходимого пространства.</summary>
        public int RegionCount { get; }
        
        /// <summary>Сколько всего компонент во всех чанках.</summary>
        public int ComponentTotal => centroids.Length;
        
        private NavMesh3DRegions(int[] componentBase, int[] componentCount,
            ushort[][] labels, NavMesh3DRegionManager unionFind, int regionCount,
            float3[] centroids, int[] adjStart, int[] adjList, float[] adjCost)
        {
            this.componentBase = componentBase;
            this.componentCount = componentCount;
            this.labels = labels;
            this.unionFind = unionFind;
            RegionCount = regionCount;
            this.centroids = centroids;
            this.adjStart = adjStart;
            this.adjList = adjList;
            this.adjCost = adjCost;
        }
        
        /// <summary>Глобальный индекс компоненты узла. -1 — непроходим или неизвестен.</summary>
        public int GetComponent(int chunkIndex, int localIndex)
        {
            if (componentCount[chunkIndex] == 0)
                return -1;
            
            var chunkLabels = labels[chunkIndex];
            if (chunkLabels == null)
                return componentBase[chunkIndex];
            
            int local = chunkLabels[localIndex];
            return local == NoLabel ? -1 : componentBase[chunkIndex] + local;
        }
        
        /// <summary>
        /// Дейкстра по графу компонент от цели: расстояние (в узлах) от центра каждой
        /// компоненты до центра целевой. Недостижимые — +∞.
        /// </summary>
        public void ComputeDistancesTo(int goal, float[] dist, FastPriorityQueue queue)
        {
            for (int i = 0; i < dist.Length; i++)
                dist[i] = float.PositiveInfinity;
            
            queue.Clear();
            dist[goal] = 0f;
            queue.Enqueue(goal, 0f);
            
            while (queue.Count > 0)
            {
                int c = queue.Dequeue();
                float dc = dist[c];
                
                for (int e = adjStart[c]; e < adjStart[c + 1]; e++)
                {
                    int n = adjList[e];
                    float nd = dc + adjCost[e];
                    if (nd < dist[n])
                    {
                        dist[n] = nd;
                        queue.Enqueue(n, nd);
                    }
                }
            }
        }
        
        /// <summary>
        /// Регион узла. -1 — узел непроходим или его статус изменился после построения
        /// (carving), тогда связность неизвестна.
        /// </summary>
        public int GetRegion(int chunkIndex, int localIndex)
        {
            int count = componentCount[chunkIndex];
            if (count == 0)
                return -1;
            
            int local = 0;
            var chunkLabels = labels[chunkIndex];
            if (chunkLabels != null)
            {
                local = chunkLabels[localIndex];
                if (local == NoLabel)
                    return -1;
            }
            
            return unionFind.Find(componentBase[chunkIndex] + local);
        }
        
        /// <summary>Регион первой компоненты чанка (для гизмо). -1 если чанк заблокирован.</summary>
        public int GetChunkRegion(int chunkIndex)
        {
            return componentCount[chunkIndex] == 0 ? -1 : unionFind.Find(componentBase[chunkIndex]);
        }
        
        /// <summary>
        /// Строит регионы. chunks — плоский массив, индекс чанка y * cx * cz + x * cz + z.
        /// </summary>
        public static NavMesh3DRegions Build(NavMesh3DChunk[] chunks, int3 chunkCount)
        {
            int total = chunks.Length;
            var componentBase = new int[total];
            var componentCount = new int[total];
            var labels = new ushort[total][];
            
            // 1. Компоненты внутри чанков
            var queue = new int[NavMesh3DChunk.NodeCount];
            var scratch = new ushort[NavMesh3DChunk.NodeCount];
            var sums = new float3[NavMesh3DChunk.NodeCount];
            var sizes = new int[NavMesh3DChunk.NodeCount];
            var centroids = new List<float3>();
            int components = 0;
            int cx = chunkCount.x, cy = chunkCount.y, cz = chunkCount.z;
            
            for (int c = 0; c < total; c++)
            {
                var chunk = chunks[c];
                componentBase[c] = components;
                
                if (chunk.IsFullyBlocked)
                    continue;
                
                float3 origin = new float3(chunk.Coord.x, chunk.Coord.y, chunk.Coord.z) * Size;
                
                if (chunk.IsFullyWalkable)
                {
                    componentCount[c] = 1;
                    components++;
                    centroids.Add(origin + (Size - 1) * 0.5f);
                    continue;
                }
                
                int count = LabelChunk(chunk, scratch, queue);
                componentCount[c] = count;
                components += count;
                
                // Центры компонент
                for (int i = 0; i < count; i++) { sums[i] = float3.zero; sizes[i] = 0; }
                for (int n = 0; n < NavMesh3DChunk.NodeCount; n++)
                {
                    int l = scratch[n];
                    if (l == NoLabel) continue;
                    sums[l] += new float3((n >> 4) & 15, n >> 8, n & 15);
                    sizes[l]++;
                }
                for (int i = 0; i < count; i++)
                    centroids.Add(origin + sums[i] / sizes[i]);
                
                if (count > 1)
                    labels[c] = (ushort[])scratch.Clone();
            }
            
            var unionFind = new NavMesh3DRegionManager(components);
            int regionCount = components;
            var edges = new HashSet<long>();
            
            // 2. Склейка по граням
            for (int y = 0; y < cy; y++)
            for (int x = 0; x < cx; x++)
            for (int z = 0; z < cz; z++)
            {
                int a = y * cx * cz + x * cz + z;
                if (componentCount[a] == 0)
                    continue;
                
                if (x + 1 < cx) regionCount -= UnionFace(chunks, labels, componentBase, componentCount, unionFind, edges, a, a + cz, 0);
                if (y + 1 < cy) regionCount -= UnionFace(chunks, labels, componentBase, componentCount, unionFind, edges, a, a + cx * cz, 1);
                if (z + 1 < cz) regionCount -= UnionFace(chunks, labels, componentBase, componentCount, unionFind, edges, a, a + 1, 2);
            }
            
            // 3. Граф компонент в CSR
            var degree = new int[components + 1];
            foreach (long e in edges)
            {
                degree[(int)(e >> 32)]++;
                degree[(int)(e & 0xFFFFFFFF)]++;
            }
            var adjStart = new int[components + 1];
            for (int i = 0; i < components; i++)
                adjStart[i + 1] = adjStart[i] + degree[i];
            var fill = (int[])adjStart.Clone();
            var adjList = new int[adjStart[components]];
            var adjCost = new float[adjList.Length];
            var centroidArray = centroids.ToArray();
            foreach (long e in edges)
            {
                int u = (int)(e >> 32), v = (int)(e & 0xFFFFFFFF);
                float cost = math.distance(centroidArray[u], centroidArray[v]);
                adjList[fill[u]] = v; adjCost[fill[u]++] = cost;
                adjList[fill[v]] = u; adjCost[fill[v]++] = cost;
            }
            
            return new NavMesh3DRegions(componentBase, componentCount, labels, unionFind, regionCount,
                centroidArray, adjStart, adjList, adjCost);
        }
        
        /// <summary>Flood fill внутри чанка. Возвращает число компонент, метки пишет в result.</summary>
        private static int LabelChunk(NavMesh3DChunk chunk, ushort[] result, int[] queue)
        {
            for (int i = 0; i < result.Length; i++)
                result[i] = NoLabel;
            
            int count = 0;
            for (int seed = 0; seed < NavMesh3DChunk.NodeCount; seed++)
            {
                if (result[seed] != NoLabel || !chunk.IsWalkable(seed))
                    continue;
                
                ushort label = (ushort)count++;
                int head = 0, tail = 0;
                result[seed] = label;
                queue[tail++] = seed;
                
                while (head < tail)
                {
                    int n = queue[head++];
                    int ly = n >> 8, lx = (n >> 4) & 15, lz = n & 15;
                    
                    if (lx > 0) Visit(n - Size, chunk, result, queue, ref tail, label);
                    if (lx < Size - 1) Visit(n + Size, chunk, result, queue, ref tail, label);
                    if (ly > 0) Visit(n - Size * Size, chunk, result, queue, ref tail, label);
                    if (ly < Size - 1) Visit(n + Size * Size, chunk, result, queue, ref tail, label);
                    if (lz > 0) Visit(n - 1, chunk, result, queue, ref tail, label);
                    if (lz < Size - 1) Visit(n + 1, chunk, result, queue, ref tail, label);
                }
            }
            
            return count;
        }
        
        private static void Visit(int n, NavMesh3DChunk chunk, ushort[] result, int[] queue, ref int tail, ushort label)
        {
            if (result[n] != NoLabel || !chunk.IsWalkable(n))
                return;
            result[n] = label;
            queue[tail++] = n;
        }
        
        /// <summary>
        /// Объединяет компоненты двух соседних чанков через общую грань.
        /// axis: 0 — b по +X от a, 1 — по +Y, 2 — по +Z. Возвращает число слияний.
        /// </summary>
        private static int UnionFace(NavMesh3DChunk[] chunks, ushort[][] labels, int[] componentBase,
            int[] componentCount, NavMesh3DRegionManager unionFind, HashSet<long> edges, int a, int b, int axis)
        {
            if (componentCount[b] == 0)
                return 0;
            
            var chunkA = chunks[a];
            var chunkB = chunks[b];
            var labelsA = labels[a];
            var labelsB = labels[b];
            int merges = 0;
            
            for (int u = 0; u < Size; u++)
            for (int v = 0; v < Size; v++)
            {
                int ia, ib;
                switch (axis)
                {
                    case 0: // x: 15 | 0, u = y, v = z
                        ia = u * Size * Size + (Size - 1) * Size + v;
                        ib = u * Size * Size + v;
                        break;
                    case 1: // y: 15 | 0, u = x, v = z
                        ia = (Size - 1) * Size * Size + u * Size + v;
                        ib = u * Size + v;
                        break;
                    default: // z: 15 | 0, u = y, v = x
                        ia = u * Size * Size + v * Size + (Size - 1);
                        ib = u * Size * Size + v * Size;
                        break;
                }
                
                if (!chunkA.IsWalkable(ia) || !chunkB.IsWalkable(ib))
                    continue;
                
                int ca = componentBase[a] + (labelsA != null ? labelsA[ia] : 0);
                int cb = componentBase[b] + (labelsB != null ? labelsB[ib] : 0);
                edges.Add(((long)math.min(ca, cb) << 32) | (uint)math.max(ca, cb));
                if (unionFind.Union(ca, cb))
                    merges++;
            }
            
            return merges;
        }
    }
}
