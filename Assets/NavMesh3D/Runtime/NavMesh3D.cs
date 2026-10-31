using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;

namespace NavMesh3DSystem
{
    /// <summary>
    /// Главный компонент 3D навигационной сетки.
    /// 
    /// ИСПОЛЬЗОВАНИЕ:
    /// 1. Создай пустой GameObject
    /// 2. Добавь этот компонент
    /// 3. Настрой размеры и параметры
    /// 4. Нажми "Bake" в инспекторе (или вызови Bake() из кода)
    /// 
    /// Компонент автоматически:
    /// - Регистрируется как синглтон (NavMesh3D.Instance)
    /// - Создаёт чанки при запекании
    /// - Строит регионы для быстрой проверки связности
    /// - Предоставляет API для поиска пути
    /// </summary>
    [ExecuteInEditMode] // Позволяет видеть Gizmos в редакторе
    public class NavMesh3D : MonoBehaviour
    {
        // ===== Синглтон =====
        
        /// <summary>
        /// Глобальный доступ к навмешу.
        /// Позволяет агентам находить навмеш без явных ссылок.
        /// </summary>
        public static NavMesh3D Instance { get; private set; }
        
        // ===== Настройки (видны в инспекторе) =====
        
        [Header("Volume Settings")]
        [Tooltip("Размер навигационного пространства в мировых единицах")]
        public Vector3 size = new Vector3(50, 50, 50);
        
        [Header("Navigation Settings")]
        [Tooltip("Размер одной ячейки. Меньше = точнее, но больше памяти")]
        [Range(0.1f, 2f)]
        public float cellSize = 0.5f;
        
        [Tooltip("Радиус агента. Узлы ближе этого расстояния к препятствиям будут заблокированы")]
        [Range(0.1f, 2f)]
        public float agentRadius = 0.5f;
        
        [Tooltip("Какие слои считать препятствиями")]
        public LayerMask obstacleLayers = ~0; // Все слои по умолчанию
        
        [Header("Debug Visualization (объект должен быть выделен)")]
        [Tooltip("Slice — срез одним слоем, Hull — оболочка проходимого объёма, Nodes — пограничные узлы у камеры")]
        public NavMesh3DDebugMode debugMode = NavMesh3DDebugMode.Slice;
        
        [Tooltip("Красить по регионам: у каждой связной области свой цвет, мелкие — серым")]
        public bool colorByRegion = true;
        
        [Range(0f, 1f)]
        [Tooltip("Прозрачность плиток среза и оболочки")]
        public float debugAlpha = 0.45f;
        
        [Tooltip("Как часто (сек) обновлять оболочку Hull, когда препятствия меняют сетку")]
        public float hullRefreshInterval = 0.25f;
        
        [Tooltip("Ось среза: Y — горизонтальный срез")]
        public NavMesh3DSliceAxis sliceAxis = NavMesh3DSliceAxis.Y;
        
        [Tooltip("Срез идёт через камеру Scene View (удобно летать по пещере)")]
        public bool sliceFollowsCamera = true;
        
        [Range(0f, 1f)]
        [Tooltip("Положение среза в объёме, если он не следует за камерой")]
        public float slicePosition = 0.5f;
        
        [Tooltip("Показывать в срезе заблокированные узлы (тёмно-красным)")]
        public bool sliceShowBlocked = false;
        
        [Tooltip("Рамки чанков: красный — весь заблокирован, зелёный — весь проходим, жёлтый — смешанный")]
        public bool showChunks = false;
        
        [Tooltip("Радиус отрисовки узлов от камеры в режиме Nodes (в метрах)")]
        [Range(1f, 50f)]
        public float gridDrawRadius = 10f;
        
        [Header("Baking")]
        [Tooltip("Автоматически запекать при старте игры")]
        public bool bakeOnStart = true;
        
        [Tooltip("При запекании из поля пещеры добавлять коллайдеры сцены из Obstacle Layers " +
                 "(кроме самой пещеры и агентов): ящики, двери, большие пропсы")]
        public bool fieldIncludesColliders = true;
        
        [Header("Profiling")]
        [Tooltip("Раз в Stats Interval секунд писать в консоль сводку по поиску путей")]
        public bool logPathStats = false;
        
        [Min(1f)]
        public float statsInterval = 5f;
        
        private float nextStatsTime;
        
        // ===== Данные (создаются при запекании) =====
        
        /// <summary>3D массив чанков</summary>
        private NavMesh3DChunk[,,] chunks;
        
        /// <summary>Те же чанки плоским массивом: индекс y * cx * cz + x * cz + z</summary>
        private NavMesh3DChunk[] flatChunks;
        
        /// <summary>Связность проходимого пространства</summary>
        private NavMesh3DRegions regions;
        
        /// <summary>Количество чанков по каждой оси</summary>
        private int3 chunkCount;
        
        /// <summary>Границы навмеша в мировых координатах</summary>
        private NavMesh3DBounds bounds;
        
        /// <summary>Поисковик путей</summary>
        private NavMesh3DPathfinder pathfinder;
        
        /// <summary>Навмеш запечён и готов к использованию</summary>
        public bool IsBaked { get; private set; }
        
        // ===== Публичные свойства =====
        
        /// <summary>Количество чанков по каждой оси</summary>
        public int3 ChunkCount => chunkCount;
        
        /// <summary>Общее количество чанков</summary>
        public int TotalChunkCount => chunkCount.x * chunkCount.y * chunkCount.z;
        
        /// <summary>Общее количество узлов</summary>
        public int TotalNodeCount => TotalChunkCount * NavMesh3DChunk.NodeCount;
        
        /// <summary>Количество регионов (связных областей)</summary>
        public int RegionCount => regions?.RegionCount ?? 0;
        
        /// <summary>Чанки плоским массивом для горячих циклов (pathfinder).</summary>
        internal NavMesh3DChunk[] FlatChunks => flatChunks;
        
        internal NavMesh3DRegions Regions => regions;
        
        /// <summary>Номер запекания: препятствия по нему понимают, что сетка пересоздана.</summary>
        public int BakeVersion => bakeVersion;
        
        /// <summary>Растёт при каждом изменении узлов препятствиями (кэши поиска сбрасываются).</summary>
        public int GridVersion { get; private set; }
        
        /// <summary>Последнее запекание было из поля расстояний (пещера), а не через физику.</summary>
        public bool BakedFromField { get; private set; }
        
        // Вырезания препятствиями: сколько препятствий накрывают узел и каким он был после запекания.
        // Счётчик нужен, чтобы при уходе одного из двух перекрывающихся препятствий узел не открылся.
        private readonly Dictionary<long, (int count, bool baked)> carves = new Dictionary<long, (int, bool)>();
        
        private static long CarveKey(int3 p) => ((long)p.x & 0xFFFFF) | (((long)p.y & 0xFFFFF) << 20) | (((long)p.z & 0xFFFFF) << 40);
        
        /// <summary>Препятствие накрыло узел. Вызывать парно с UncarveNode.</summary>
        public void CarveNode(int3 p)
        {
            long key = CarveKey(p);
            if (carves.TryGetValue(key, out var e))
            {
                carves[key] = (e.count + 1, e.baked);
                return;
            }
            
            carves[key] = (1, IsWalkableAbsolute(p));
            SetWalkableAbsolute(p, false);
            GridVersion++;
        }
        
        /// <summary>Препятствие ушло с узла. Узел открывается, только когда его не накрывает ни одно.</summary>
        public void UncarveNode(int3 p)
        {
            long key = CarveKey(p);
            if (!carves.TryGetValue(key, out var e))
                return; // сетку перезапекли — узла в журнале уже нет
            
            if (e.count > 1)
            {
                carves[key] = (e.count - 1, e.baked);
                return;
            }
            
            carves.Remove(key);
            SetWalkableAbsolute(p, e.baked);
            GridVersion++;
        }
        
        /// <summary>Сделать узел проходимым в обход журнала (ClearArea при физическом запекании).</summary>
        internal void ForceWalkable(int3 p)
        {
            SetWalkableAbsolute(p, true);
            GridVersion++;
        }
        
        /// <summary>Граф дверей для HPA*</summary>
        internal NavMesh3DHierarchy Hierarchy => hierarchy;
        
        private NavMesh3DHierarchy hierarchy;
        
        /// <summary>
        /// Внешний источник сетки (например пещера с полем вокселей). Если задан,
        /// Bake() сначала просит его, и только при отказе запекает через физику.
        /// </summary>
        public INavMesh3DBakeSource BakeSource { get; set; }
        
        /// <summary>Доступ к Pathfinder для расширенных запросов</summary>
        public NavMesh3DPathfinder Pathfinder => pathfinder;
        
        /// <summary>Границы навмеша</summary>
        public NavMesh3DBounds Bounds => bounds;
        
        // ===== Unity Lifecycle =====
        
        private void Awake()
        {
            // Регистрируем синглтон
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("NavMesh3D: Multiple instances detected. Using the first one.");
                return;
            }
            Instance = this;
        }
        
        private void Start()
        {
            if (Application.isPlaying && bakeOnStart)
            {
                Bake();
            }
        }
        
        private void Update()
        {
            if (!logPathStats || !Application.isPlaying || pathfinder == null || Time.time < nextStatsTime)
                return;
            
            nextStatsTime = Time.time + statsInterval;
            var s = pathfinder.Stats;
            if (s.paths == 0)
                return;
            
            Debug.Log($"NavMesh3D пути за {statsInterval:F0} с: {s.paths} шт. " +
                      $"(найдено {s.found}, нет пути {s.noPath}, лимит итераций {s.iterationLimit}, " +
                      $"старт/цель в стене {s.blockedEndpoints}, вне объёма {s.invalidEndpoints}, прочее {s.otherFailures}). " +
                      $"Время: среднее {s.totalMs / s.paths:F3} мс, максимум {s.maxMs:F2} мс, худший кадр {s.maxFrameMs:F2} мс " +
                      $"({s.maxPathsPerFrame} поисков). Итерации: среднее {s.totalIterations / s.paths}, максимум {s.maxIterations}.");
            pathfinder.ResetStats();
        }
        
        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }
        
        private void OnValidate()
        {
            // Ограничиваем минимальные значения
            size = Vector3.Max(size, Vector3.one * cellSize * NavMesh3DSettings.ChunkSize);
            cellSize = Mathf.Max(cellSize, 0.1f);
            agentRadius = Mathf.Max(agentRadius, 0.1f);
        }
        
        // ===== Публичные методы =====
        
        /// <summary>
        /// Запекает навигационную сетку.
        /// Создаёт чанки, проверяет коллизии через Job System, строит регионы.
        /// </summary>
        public void Bake()
        {
            if (BakeSource != null && BakeSource.TryBake(this))
                return;
            
            BakedFromField = false;
            
            // Сохраняем границы
            UpdateBounds();
            
            // Вычисляем количество чанков
            CalculateChunkCount();
            
            // Создаём чанки
            CreateChunks();
            
            // Запекаем коллизии через Job System
            var bakeResult = NavMesh3DBaker.Bake(
                chunks,
                bounds,
                chunkCount,
                cellSize,
                agentRadius,
                obstacleLayers,
                showProgress: true
            );
            
            FinishBake(bakeResult);
        }
        
        /// <summary>
        /// Запекает навмеш из поля расстояний вместо физики (для воксельных пещер).
        /// Объём подгоняется под поле: центр и поворот берутся из space, размер — из поля.
        /// </summary>
        /// <param name="field">Поле расстояний в локальных координатах space</param>
        /// <param name="space">Объект, в чьих координатах задано поле (корень пещеры). Масштаб 1.</param>
        public void BakeFromDistanceField(NavMesh3DDistanceField field, Transform space)
        {
            transform.SetPositionAndRotation(space.TransformPoint(field.Center), space.rotation);
            size = field.Extent;
            
            UpdateBounds();
            CalculateChunkCount();
            CreateChunks();
            
            // Угол сетки в координатах поля: сетка центрирована на центре поля
            float3 gridSize = (float3)chunkCount * NavMesh3DSettings.ChunkSize * cellSize;
            float3 gridCorner = (float3)field.Center - gridSize * 0.5f;
            
            var bakeResult = NavMesh3DDistanceFieldBaker.Bake(
                chunks, chunkCount, cellSize, agentRadius, gridCorner, field);
            BakedFromField = true;
            
            if (bakeResult.success && fieldIncludesColliders)
            {
                int blocked = ApplyColliderObstacles(space);
                bakeResult.walkableNodes -= blocked;
                bakeResult.blockedNodes += blocked;
            }
            
            FinishBake(bakeResult);
        }
        
        /// <summary>
        /// Поле знает только камень пещеры. Остальные коллайдеры в объёме (кроме детей space
        /// и агентов) проверяем физикой, но только узлы в их bounds — это быстро.
        /// </summary>
        /// <returns>Сколько узлов заблокировано</returns>
        private int ApplyColliderObstacles(Transform space)
        {
            // Пещера и пропсы могли появиться в этом же кадре
            Physics.SyncTransforms();
            
            float3 gridSize = (float3)chunkCount * NavMesh3DSettings.ChunkSize * cellSize;
            var colliders = Physics.OverlapBox(bounds.center, gridSize * 0.5f, bounds.rotation,
                obstacleLayers, QueryTriggerInteraction.Ignore);
            
            int3 limit = chunkCount * NavMesh3DSettings.ChunkSize;
            var hits = new Collider[32];
            int blocked = 0;
            
            foreach (var col in colliders)
            {
                // Стены пещеры уже в поле, агенты не должны вырезать сетку под собой
                // Динамические препятствия вырежут себя сами (NavMesh3DObstacle)
                if (col.transform.IsChildOf(space) || col.GetComponentInParent<NavMesh3DAgent>() != null ||
                    col.GetComponentInParent<NavMesh3DObstacle>() != null)
                    continue;
                
                Bounds b = col.bounds;
                b.Expand(agentRadius * 2f + cellSize);
                
                // Диапазон узлов по 8 углам bounds (объём может быть повёрнут)
                int3 min = limit, max = -1;
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3(
                        (i & 1) == 0 ? b.min.x : b.max.x,
                        (i & 2) == 0 ? b.min.y : b.max.y,
                        (i & 4) == 0 ? b.min.z : b.max.z);
                    int3 n = WorldToNode(corner).ToAbsolute();
                    min = math.min(min, n);
                    max = math.max(max, n);
                }
                min = math.max(min, 0);
                max = math.min(max, limit - 1);
                
                for (int x = min.x; x <= max.x; x++)
                for (int y = min.y; y <= max.y; y++)
                for (int z = min.z; z <= max.z; z++)
                {
                    var p = new int3(x, y, z);
                    if (!IsWalkableAbsolute(p))
                        continue;
                    
                    Vector3 pos = NodeToWorld(GlobalNodeCoord.FromAbsolute(p));
                    int count = Physics.OverlapSphereNonAlloc(pos, agentRadius, hits, obstacleLayers, QueryTriggerInteraction.Ignore);
                    for (int h = 0; h < count; h++)
                    {
                        if (hits[h] != col) continue;
                        SetWalkableAbsolute(p, false);
                        blocked++;
                        break;
                    }
                }
            }
            
            return blocked;
        }
        
        internal void SetWalkableAbsolute(int3 p, bool walkable)
        {
            int3 cc = p >> 4;
            int local = ((p.y & 15) << 8) | ((p.x & 15) << 4) | (p.z & 15);
            flatChunks[cc.y * chunkCount.x * chunkCount.z + cc.x * chunkCount.z + cc.z].SetWalkable(local, walkable);
        }
        
        /// <summary>
        /// Перестраивает связность. Вызывай после крупных изменений сетки
        /// (например после ClearArea у препятствий), иначе IsReachable может ошибаться.
        /// </summary>
        public void RebuildRegions()
        {
            if (chunks == null) return;
            BuildRegions();
        }
        
        private void FinishBake(NavMesh3DBaker.BakeResult bakeResult)
        {
            bakeVersion++;
            GridVersion++;
            carves.Clear();

            if (!bakeResult.success)
            {
                Debug.LogError($"NavMesh3D: Bake failed - {bakeResult.errorMessage}");
                Clear();
                return;
            }
            
            var regionTimer = System.Diagnostics.Stopwatch.StartNew();
            BuildRegions();
            regionTimer.Stop();
            
            // Создаём поисковик путей
            pathfinder = new NavMesh3DPathfinder(this);
            
            IsBaked = true;
            
            Debug.Log($"NavMesh3D: Baked in {bakeResult.timeSeconds:F2}s (+ regions и HPA* {regionTimer.ElapsedMilliseconds} ms). " +
                     $"Chunks: {TotalChunkCount}, " +
                     $"Nodes: {bakeResult.totalNodes:N0} (walkable: {bakeResult.walkableNodes:N0}, blocked: {bakeResult.blockedNodes:N0}), " +
                     $"Regions: {RegionCount}, HPA* doors: {hierarchy?.Count ?? 0}");
        }
        
        /// <summary>
        /// Очищает навмеш.
        /// </summary>
        public void Clear()
        {
            chunks = null;
            flatChunks = null;
            regions = null;
            hierarchy = null;
            pathfinder = null;
            IsBaked = false;
        }
        
        /// <summary>
        /// Ищет путь между двумя точками.
        /// Простой API — возвращает список точек или null.
        /// </summary>
        /// <param name="from">Начальная точка</param>
        /// <param name="to">Конечная точка</param>
        /// <returns>Список точек пути или null если путь не найден</returns>
        public List<Vector3> FindPath(Vector3 from, Vector3 to)
        {
            if (pathfinder == null) return null;
            
            var request = PathRequest.Create(from, to);
            var response = pathfinder.FindPath(request);
            
            return response.Success ? response.path : null;
        }
        
        /// <summary>
        /// Ищет путь с полной информацией о результате.
        /// </summary>
        public PathResponse FindPathDetailed(Vector3 from, Vector3 to)
        {
            if (pathfinder == null)
            {
                return new PathResponse { result = PathResult.NotBaked };
            }
            
            var request = PathRequest.Create(from, to);
            return pathfinder.FindPath(request);
        }
        
        /// <summary>
        /// Ищет путь с кастомными параметрами.
        /// </summary>
        public PathResponse FindPath(PathRequest request)
        {
            if (pathfinder == null)
            {
                return new PathResponse { result = PathResult.NotBaked };
            }
            
            return pathfinder.FindPath(request);
        }
        
        /// <summary>
        /// Проверяет, достижима ли точка B из точки A.
        /// Быстрая проверка через регионы — O(1).
        /// </summary>
        public bool IsReachable(Vector3 from, Vector3 to)
        {
            if (!IsBaked) return false;
            
            var a = WorldToNode(from);
            var b = WorldToNode(to);
            if (!IsValidNodeCoord(a) || !IsValidNodeCoord(b))
                return false;
            
            return AreConnected(a, b);
        }
        
        /// <summary>
        /// Точная проверка связности двух узлов через регионы. Узел с неизвестным регионом
        /// (непроходимый или изменённый carving'ом) считается связанным — решит A*.
        /// </summary>
        public bool AreConnected(GlobalNodeCoord a, GlobalNodeCoord b)
        {
            int ra = regions.GetRegion(ChunkIndex(a.chunk), a.local.ToIndex());
            int rb = regions.GetRegion(ChunkIndex(b.chunk), b.local.ToIndex());
            return ra < 0 || rb < 0 || ra == rb;
        }
        
        /// <summary>Регион узла по абсолютным координатам (для отладки). -1 — неизвестен.</summary>
        internal int GetRegionAbsolute(int3 p)
        {
            int3 cc = p >> 4;
            int chunk = cc.y * chunkCount.x * chunkCount.z + cc.x * chunkCount.z + cc.z;
            return regions.GetRegion(chunk, ((p.y & 15) << 8) | ((p.x & 15) << 4) | (p.z & 15));
        }
        
        internal int ChunkIndex(ChunkCoord c) => c.y * chunkCount.x * chunkCount.z + c.x * chunkCount.z + c.z;
        
        /// <summary>
        /// Проверяет, проходима ли точка в мировых координатах.
        /// </summary>
        public bool IsWalkable(Vector3 worldPosition)
        {
            if (!IsBaked) return false;
            
            var nodeCoord = WorldToNode(worldPosition);
            if (!IsValidNodeCoord(nodeCoord))
                return false;
            
            var chunk = GetChunkAt(nodeCoord.chunk);
            return chunk?.IsWalkable(nodeCoord.local) ?? false;
        }
        
        /// <summary>
        /// Находит ближайшую проходимую точку к заданной.
        /// </summary>
        public Vector3 GetNearestWalkable(Vector3 worldPosition, float maxDistance = 10f)
        {
            if (!IsBaked) return worldPosition;
            
            if (IsWalkable(worldPosition))
                return worldPosition;
            
            return TryFindNearestWalkableNode(WorldToNode(worldPosition), Mathf.CeilToInt(maxDistance / cellSize), out var node)
                ? NodeToWorld(node)
                : worldPosition;
        }
        
        /// <summary>
        /// Ищет ближайший проходимый узел расширяющимися кубическими оболочками.
        /// Внутри оболочки выбирает узел, ближайший по евклиду.
        /// </summary>
        public bool TryFindNearestWalkableNode(GlobalNodeCoord from, int maxSteps, out GlobalNodeCoord result)
        {
            result = from;
            if (!IsBaked) return false;
            
            int3 c = from.ToAbsolute();
            int3 limit = chunkCount * NavMesh3DSettings.ChunkSize;
            
            if (math.all(c >= 0) && math.all(c < limit) && IsWalkableAbsolute(c))
                return true;
            
            for (int r = 1; r <= maxSteps; r++)
            {
                int bestDist = int.MaxValue;
                int3 best = default;
                
                for (int dx = -r; dx <= r; dx++)
                for (int dy = -r; dy <= r; dy++)
                for (int dz = -r; dz <= r; dz++)
                {
                    // Только оболочка куба радиуса r
                    if (math.abs(dx) != r && math.abs(dy) != r && math.abs(dz) != r)
                        continue;
                    
                    int d2 = dx * dx + dy * dy + dz * dz;
                    if (d2 >= bestDist)
                        continue;
                    
                    int3 p = c + new int3(dx, dy, dz);
                    if (math.any(p < 0) || math.any(p >= limit) || !IsWalkableAbsolute(p))
                        continue;
                    
                    bestDist = d2;
                    best = p;
                }
                
                if (bestDist != int.MaxValue)
                {
                    result = GlobalNodeCoord.FromAbsolute(best);
                    return true;
                }
            }
            
            return false;
        }
        
        internal bool IsWalkableAbsolute(int3 p)
        {
            int3 cc = p >> 4;
            int local = ((p.y & 15) << 8) | ((p.x & 15) << 4) | (p.z & 15);
            return flatChunks[cc.y * chunkCount.x * chunkCount.z + cc.x * chunkCount.z + cc.z].IsWalkable(local);
        }
        
        /// <summary>
        /// Получает чанк по координатам.
        /// </summary>
        public NavMesh3DChunk GetChunkAt(ChunkCoord coord)
        {
            if (!IsBaked) return null;
            
            if (coord.x < 0 || coord.x >= chunkCount.x ||
                coord.y < 0 || coord.y >= chunkCount.y ||
                coord.z < 0 || coord.z >= chunkCount.z)
                return null;
            
            return chunks[coord.x, coord.y, coord.z];
        }
        
        // ===== Приватные методы =====
        
        private void UpdateBounds()
        {
            bounds = new NavMesh3DBounds
            {
                center = transform.position,
                size = size,
                rotation = transform.rotation
            };
        }
        
        private void CalculateChunkCount()
        {
            // Сколько узлов помещается по каждой оси
            int3 nodeCount = (int3)math.ceil((float3)size / cellSize);
            
            // Сколько чанков (округляем вверх)
            chunkCount = (nodeCount + NavMesh3DSettings.ChunkSize - 1) / NavMesh3DSettings.ChunkSize;
            
            // Минимум 1 чанк по каждой оси
            chunkCount = math.max(chunkCount, new int3(1, 1, 1));
        }
        
        private void CreateChunks()
        {
            chunks = new NavMesh3DChunk[chunkCount.x, chunkCount.y, chunkCount.z];
            flatChunks = new NavMesh3DChunk[TotalChunkCount];
            
            for (int x = 0; x < chunkCount.x; x++)
            {
                for (int y = 0; y < chunkCount.y; y++)
                {
                    for (int z = 0; z < chunkCount.z; z++)
                    {
                        var chunk = new NavMesh3DChunk(new ChunkCoord(x, y, z));
                        chunks[x, y, z] = chunk;
                        flatChunks[y * chunkCount.x * chunkCount.z + x * chunkCount.z + z] = chunk;
                    }
                }
            }
        }
        
        private void BuildRegions()
        {
            regions = NavMesh3DRegions.Build(flatChunks, chunkCount);
            hierarchy = NavMesh3DHierarchy.Build(flatChunks, chunkCount, regions);
        }
        
        /// <summary>
        /// Конвертирует мировые координаты в координаты узла.
        /// </summary>
        public GlobalNodeCoord WorldToNode(Vector3 worldPosition)
        {
            // Реальный размер сетки
            float3 gridSize = (float3)chunkCount * NavMesh3DSettings.ChunkSize * cellSize;
            
            // Переводим в локальные координаты volume
            float3 localPos = math.mul(
                math.inverse(bounds.rotation), 
                (float3)worldPosition - bounds.center
            );
            
            // Смещаем от центра сетки к углу
            localPos += gridSize * 0.5f;
            
            // Конвертируем в координаты узла
            int3 nodeCoord = (int3)math.floor(localPos / cellSize);
            
            return GlobalNodeCoord.FromAbsolute(nodeCoord);
        }
        
        /// <summary>
        /// Конвертирует координаты узла в мировые координаты.
        /// </summary>
        public Vector3 NodeToWorld(GlobalNodeCoord nodeCoord)
        {
            int3 absolute = nodeCoord.ToAbsolute();
            
            // Реальный размер сетки (может быть больше bounds.size из-за округления чанков)
            float3 gridSize = (float3)chunkCount * NavMesh3DSettings.ChunkSize * cellSize;
            
            // Позиция узла в локальных координатах (от угла сетки)
            float3 localPos = (float3)absolute * cellSize + cellSize * 0.5f;
            
            // Смещаем чтобы центр сетки совпадал с центром bounds
            localPos -= gridSize * 0.5f;
            
            // Переводим в мировые координаты
            return (Vector3)(bounds.center + math.mul(bounds.rotation, localPos));
        }
        
        private bool IsValidNodeCoord(GlobalNodeCoord coord)
        {
            return coord.chunk.x >= 0 && coord.chunk.x < chunkCount.x &&
                   coord.chunk.y >= 0 && coord.chunk.y < chunkCount.y &&
                   coord.chunk.z >= 0 && coord.chunk.z < chunkCount.z &&
                   coord.local.IsValid();
        }
        
        // ===== Gizmos =====
        
        private void OnDrawGizmos()
        {
            // Рисуем границы volume
            Gizmos.color = new Color(0.5f, 0.9f, 0.9f, 0.3f);
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, size);
            Gizmos.matrix = Matrix4x4.identity;
        }
        
        // Кэш мешей для гизмо: пересобираются при смене настроек или после запекания
        private int bakeVersion;
        private System.Collections.Generic.List<NavMesh3DDebugMeshes.Batch> debugBatches;
        private string debugKey;
        private int debugGridVersion;
        private float debugBuildTime;
        
        /// <summary>Пересобрать отладочные меши (например после изменений сетки препятствиями).</summary>
        public void RefreshDebugView() => debugKey = null;
        
        private void OnDisable()
        {
            NavMesh3DDebugMeshes.Destroy(debugBatches);
            debugKey = null;
        }
        
        private void OnDrawGizmosSelected()
        {
            if (!IsBaked) return;
            
            if (showChunks)
                DrawChunkGizmos();
            
            switch (debugMode)
            {
                case NavMesh3DDebugMode.Slice:
                case NavMesh3DDebugMode.Hull:
                    DrawDebugMeshes();
                    break;
                case NavMesh3DDebugMode.Nodes:
                    DrawGridGizmos();
                    break;
            }
        }
        
        private void DrawDebugMeshes()
        {
            int sliceLayer = GetSliceLayer();
            string key = debugMode == NavMesh3DDebugMode.Slice
                ? $"{bakeVersion}|{GridVersion}|S|{sliceAxis}|{sliceLayer}|{colorByRegion}|{sliceShowBlocked}"
                : $"{bakeVersion}|H|{colorByRegion}";
            
            // Оболочка обходит всю сетку, поэтому после вырезов препятствиями
            // пересобирается не чаще hullRefreshInterval, а не каждый кадр
            bool hullStale = debugMode == NavMesh3DDebugMode.Hull && debugGridVersion != GridVersion
                && Time.realtimeSinceStartup - debugBuildTime >= hullRefreshInterval;
            
            if (key != debugKey || debugBatches == null || hullStale)
            {
                NavMesh3DDebugMeshes.Destroy(debugBatches);
                debugBatches = debugMode == NavMesh3DDebugMode.Slice
                    ? NavMesh3DDebugMeshes.BuildSlice(this, sliceAxis, sliceLayer, colorByRegion, sliceShowBlocked)
                    : NavMesh3DDebugMeshes.BuildHull(this, colorByRegion);
                debugKey = key;
                debugGridVersion = GridVersion;
                debugBuildTime = Time.realtimeSinceStartup;
            }
            
            // Меши построены от угла сетки
            float3 gridSize = (float3)chunkCount * NavMesh3DSettings.ChunkSize * cellSize;
            Gizmos.matrix = Matrix4x4.TRS(bounds.center, bounds.rotation, Vector3.one)
                          * Matrix4x4.Translate(-(Vector3)(gridSize * 0.5f));
            
            foreach (var batch in debugBatches)
            {
                var c = batch.color;
                c.a = debugAlpha;
                Gizmos.color = c;
                Gizmos.DrawMesh(batch.mesh);
            }
            
            Gizmos.matrix = Matrix4x4.identity;
            
            #if UNITY_EDITOR
            // Легенда: регионы по размеру
            var sb = new System.Text.StringBuilder();
            sb.Append(debugMode == NavMesh3DDebugMode.Slice ? $"Срез {sliceAxis} = {sliceLayer}" : "Оболочка");
            sb.Append($", регионов: {RegionCount}");
            foreach (var batch in debugBatches)
            {
                sb.Append($"\n{batch.label}: {batch.nodeCount} узл.");
            }
            var style = new GUIStyle(UnityEditor.EditorStyles.helpBox) { richText = false, fontSize = 11 };
            UnityEditor.Handles.Label(transform.position + transform.up * (size.y * 0.5f), sb.ToString(), style);
            #endif
        }
        
        private int GetSliceLayer()
        {
            int a = (int)sliceAxis;
            int count = chunkCount[a] * NavMesh3DSettings.ChunkSize;
            
            #if UNITY_EDITOR
            if (sliceFollowsCamera && UnityEditor.SceneView.lastActiveSceneView != null)
            {
                var cam = UnityEditor.SceneView.lastActiveSceneView.camera.transform.position;
                return math.clamp(WorldToNode(cam).ToAbsolute()[a], 0, count - 1);
            }
            #endif
            
            return math.clamp((int)(slicePosition * count), 0, count - 1);
        }
        
        private void DrawChunkGizmos()
        {
            float chunkWorldSize = cellSize * NavMesh3DSettings.ChunkSize;
            
            for (int x = 0; x < chunkCount.x; x++)
            {
                for (int y = 0; y < chunkCount.y; y++)
                {
                    for (int z = 0; z < chunkCount.z; z++)
                    {
                        var chunk = chunks[x, y, z];
                        
                        // Цвет зависит от заполненности
                        float fill = (float)chunk.WalkableCount / NavMesh3DChunk.NodeCount;
                        
                        if (chunk.IsFullyBlocked)
                            Gizmos.color = new Color(1, 0, 0, 0.2f);
                        else if (chunk.IsFullyWalkable)
                            Gizmos.color = new Color(0, 1, 0, 0.2f);
                        else
                            Gizmos.color = new Color(1, 1, 0, 0.2f);
                        
                        // Центр чанка в мировых координатах
                        Vector3 chunkCenter = NodeToWorld(new GlobalNodeCoord(
                            chunk.Coord,
                            new LocalNodeCoord(
                                NavMesh3DSettings.ChunkSize / 2,
                                NavMesh3DSettings.ChunkSize / 2,
                                NavMesh3DSettings.ChunkSize / 2
                            )
                        ));
                        
                        Gizmos.DrawWireCube(chunkCenter, Vector3.one * chunkWorldSize * 0.95f);
                        
                        // Показываем процент заполнения
                        #if UNITY_EDITOR
                        if (!chunk.IsFullyBlocked && !chunk.IsFullyWalkable)
                        {
                            UnityEditor.Handles.Label(chunkCenter, $"{fill * 100:F0}%");
                        }
                        #endif
                    }
                }
            }
        }
        
        /// <summary>
        /// Пограничные проходимые узлы у камеры Scene View — те, у которых есть
        /// заблокированный сосед. Внутренние узлы воздуха не рисуются, поэтому
        /// вместо сплошной каши из сфер видна «кожа» проходимого объёма.
        /// </summary>
        private void DrawGridGizmos()
        {
            Vector3 cameraPos = transform.position;
            #if UNITY_EDITOR
            if (UnityEditor.SceneView.lastActiveSceneView != null)
                cameraPos = UnityEditor.SceneView.lastActiveSceneView.camera.transform.position;
            #endif
            
            int3 limit = chunkCount * NavMesh3DSettings.ChunkSize;
            int3 c = WorldToNode(cameraPos).ToAbsolute();
            int r = Mathf.CeilToInt(gridDrawRadius / cellSize);
            int3 min = math.max(c - r, 0);
            int3 max = math.min(c + r, limit - 1);
            float radiusSq = gridDrawRadius * gridDrawRadius;
            Vector3 cubeSize = Vector3.one * cellSize * 0.3f;
            int drawn = 0;
            
            for (int x = min.x; x <= max.x; x++)
            for (int y = min.y; y <= max.y; y++)
            for (int z = min.z; z <= max.z; z++)
            {
                var p = new int3(x, y, z);
                if (!IsWalkableAbsolute(p) || !IsBoundaryNode(p, limit))
                    continue;
                
                Vector3 pos = NodeToWorld(GlobalNodeCoord.FromAbsolute(p));
                float distSq = (pos - cameraPos).sqrMagnitude;
                if (distSq > radiusSq)
                    continue;
                
                // Ближние ярче, дальние тают
                float fade = 1f - Mathf.Sqrt(distSq) / gridDrawRadius;
                Color color = colorByRegion ? RegionColor(GetRegionAbsolute(p)) : new Color(0, 1, 1);
                color.a = 0.25f + 0.75f * fade;
                Gizmos.color = color;
                Gizmos.DrawCube(pos, cubeSize);
                drawn++;
            }
            
            #if UNITY_EDITOR
            UnityEditor.Handles.Label(cameraPos + Vector3.up * 2, $"Пограничных узлов: {drawn} в радиусе {gridDrawRadius} м");
            #endif
        }
        
        private bool IsBoundaryNode(int3 p, int3 limit)
        {
            for (int axis = 0; axis < 3; axis++)
            for (int s = -1; s <= 1; s += 2)
            {
                int3 q = p;
                q[axis] += s;
                if (math.any(q < 0) || math.any(q >= limit) || !IsWalkableAbsolute(q))
                    return true;
            }
            return false;
        }
        
        /// <summary>Стабильный цвет региона по его id (для режима Nodes).</summary>
        private static Color RegionColor(int region)
        {
            if (region < 0) return Color.gray;
            float hue = (region * 0.618034f) % 1f;
            return Color.HSVToRGB(hue, 0.75f, 1f);
        }
    }
}
