using System;
using System.Collections.Generic;
using UnityEngine;

namespace NavMesh3DSystem
{
    /// <summary>
    /// Статус агента.
    /// </summary>
    public enum AgentStatus
    {
        /// <summary>Агент стоит на месте</summary>
        Idle,
        
        /// <summary>Агент движется к цели</summary>
        Moving,
        
        /// <summary>Агент достиг цели</summary>
        ReachedDestination,
        
        /// <summary>Путь не найден</summary>
        PathFailed,
        
        /// <summary>Агент ожидает завершения поиска пути</summary>
        CalculatingPath
    }
    
    /// <summary>
    /// Компонент агента для 3D навигации.
    /// 
    /// ИСПОЛЬЗОВАНИЕ:
    /// 1. Добавь этот компонент на объект врага/NPC
    /// 2. Вызови SetDestination(targetPosition) для движения к цели
    /// 3. Агент автоматически найдёт путь и будет двигаться
    /// 
    /// ОСОБЕННОСТИ:
    /// - Автоматический поиск пути
    /// - Плавное движение и поворот
    /// - Визуализация пути в редакторе
    /// - События при достижении цели и ошибках
    /// </summary>
    public class NavMesh3DAgent : MonoBehaviour
    {
        // ===== Настройки движения =====
        
        [Header("Movement")]
        [Tooltip("Скорость движения (м/с)")]
        [Range(0.1f, 50f)]
        public float speed = 5f;
        
        [Tooltip("Скорость поворота (градусов/с). 0 = мгновенный поворот")]
        [Range(0f, 720f)]
        public float angularSpeed = 360f;
        
        [Tooltip("Ускорение (м/с²). 0 = мгновенное изменение скорости")]
        [Range(0f, 100f)]
        public float acceleration = 20f;
        
        [Tooltip("Расстояние до точки пути для перехода к следующей")]
        [Range(0.05f, 2f)]
        public float waypointThreshold = 0.3f;
        
        [Tooltip("Расстояние до цели для начала замедления (агент остановится в центре)")]
        [Range(0.1f, 5f)]
        public float stoppingDistance = 0.5f;
        
        // ===== Настройки пути =====
        
        [Header("Pathfinding")]
        [Tooltip("Автоматически пересчитывать путь при застревании")]
        public bool autoRepath = true;
        
        [Tooltip("Интервал проверки застревания (сек)")]
        [Range(0.1f, 5f)]
        public float stuckCheckInterval = 0.5f;
        
        [Tooltip("Минимальное движение за интервал, иначе считается застрявшим")]
        [Range(0.01f, 1f)]
        public float stuckThreshold = 0.1f;
        
        [Tooltip("Проверять путь на блокировку динамическими препятствиями")]
        public bool checkPathBlocked = true;
        
        [Tooltip("Интервал проверки блокировки пути (сек)")]
        [Range(0.1f, 2f)]
        public float pathCheckInterval = 0.3f;
        
        [Tooltip("Сколько точек пути вперёд проверять")]
        [Range(1, 10)]
        public int pathCheckAhead = 3;
        
        [Tooltip("Разрешить диагональное движение")]
        public bool allowDiagonal = true;
        
        [Tooltip("Максимум итераций A*")]
        public int maxIterations = 20000;
        
        // ===== Local Avoidance =====
        
        [Header("Avoidance")]
        [Tooltip("Использовать локальное избегание других агентов")]
        public bool useAvoidance = true;
        
        [Tooltip("Приоритет агента (выше = меньше уступает)")]
        [Range(0, 10)]
        public int avoidancePriority = 0;
        
        [Tooltip("Радиус агента для расчёта столкновений")]
        [Range(0.1f, 2f)]
        public float radius = 0.5f;
        
        // ===== Follow (слежение) =====
        
        [Header("Follow")]
        [Tooltip("Цель для слежения (автоматически обновляет путь)")]
        public Transform followTarget;
        
        [Tooltip("Интервал обновления пути при слежении (сек)")]
        [Range(0.1f, 2f)]
        public float followUpdateInterval = 0.5f;
        
        [Tooltip("Минимальное смещение цели для пересчёта пути (м)")]
        [Range(0.5f, 10f)]
        public float followThreshold = 2f;
        
        [Tooltip("Остановиться на этом расстоянии от цели")]
        [Range(0f, 10f)]
        public float followStopDistance = 1f;
        
        // ===== Crowd (толпа) =====
        
        [Header("Crowd")]
        [Tooltip("Автоматически смещать цель чтобы агенты не толпились в одной точке")]
        public bool useDestinationOffset = true;
        
        [Tooltip("Радиус смещения цели")]
        [Range(0.5f, 5f)]
        public float destinationOffsetRadius = 1.5f;
        
        // ===== Визуализация =====
        
        [Header("Debug")]
        [Tooltip("Показывать путь в редакторе")]
        public bool showPath = true;
        
        [Tooltip("Цвет пути")]
        public Color pathColor = Color.cyan;
        
        // ===== События =====
        
        /// <summary>Вызывается когда агент достиг цели</summary>
        public event Action OnDestinationReached;
        
        /// <summary>Вызывается когда путь не найден</summary>
        public event Action<PathResult> OnPathFailed;
        
        /// <summary>Вызывается когда новый путь успешно найден</summary>
        public event Action OnPathFound;
        
        // ===== Публичные свойства =====
        
        /// <summary>Текущий статус агента</summary>
        public AgentStatus Status { get; private set; } = AgentStatus.Idle;
        
        /// <summary>Текущая цель</summary>
        public Vector3? Destination { get; private set; }
        
        /// <summary>Есть ли активный путь</summary>
        public bool HasPath => currentPath != null && currentPath.Count > 0;
        
        /// <summary>Текущая скорость (м/с)</summary>
        public float CurrentSpeed => currentVelocity.magnitude;
        
        /// <summary>Текущий вектор скорости</summary>
        public Vector3 Velocity => currentVelocity;
        
        /// <summary>Оставшееся расстояние до цели</summary>
        public float RemainingDistance => CalculateRemainingDistance();
        
        /// <summary>Текущий путь (только для чтения)</summary>
        public IReadOnlyList<Vector3> Path => currentPath;
        
        /// <summary>Индекс текущей точки пути</summary>
        public int CurrentWaypointIndex => currentWaypointIndex;
        
        /// <summary>Следит ли агент за целью</summary>
        public bool IsFollowing => followTarget != null;
        
        /// <summary>Расстояние до цели слежения (0 если нет цели)</summary>
        public float DistanceToFollowTarget => followTarget != null 
            ? Vector3.Distance(cachedTransform.position, followTarget.position) 
            : 0f;
        
        /// <summary>
        /// Приостановить/возобновить движение.
        /// Путь сохраняется, агент просто не двигается.
        /// </summary>
        public bool IsStopped { get; set; } = false;
        
        // ===== Приватные поля =====
        
        private List<Vector3> currentPath;
        private int currentWaypointIndex;
        private Vector3 currentVelocity;
        private float currentSpeed;
        
        // Проверка застревания
        private Vector3 lastStuckCheckPosition;
        private float lastStuckCheckTime;
        private int stuckCount;
        
        // Проверка блокировки пути
        private float lastPathCheckTime;
        
        // Follow (слежение)
        private float lastFollowUpdateTime;
        private Vector3 lastFollowTargetPosition;
        
        // Асинхронный поиск
        private int pendingRequestId;
        
        // Кэш
        private NavMesh3D navMesh;
        private Transform cachedTransform;
        
        // Версия сетки на момент последней проверки пути (-1 — путь новый, проверить)
        private int lastCheckedGridVersion = -1;
        // Запекание, по которому построен текущий путь: после перезапекания путь пересчитывается
        private int pathBakeVersion = -1;
        
        /// <summary>Все включённые агенты — вместо FindObjectsByType на каждый SetDestination.</summary>
        private static readonly List<NavMesh3DAgent> ActiveAgents = new List<NavMesh3DAgent>(64);
        
        // ===== Unity Lifecycle =====
        
        private void Awake()
        {
            cachedTransform = transform;
        }
        
        private void OnEnable()
        {
            ActiveAgents.Add(this);
        }
        
        private void OnDisable()
        {
            ActiveAgents.Remove(this);
        }
        
        private void Start()
        {
            navMesh = NavMesh3D.Instance;
            
            if (navMesh == null)
            {
                Debug.LogWarning($"NavMesh3DAgent on {gameObject.name}: NavMesh3D not found in scene!");
            }
            
            // Регистрируемся в системе избегания
            if (useAvoidance)
            {
                NavMesh3DAvoidance.Instance.RegisterAgent(this, avoidancePriority);
                NavMesh3DAvoidance.Instance.SetAgentRadius(this, radius);
            }
            
            // Проверяем что слой агента не входит в obstacleLayers навмеша
            CheckAgentLayer();
        }
        
        private void CheckAgentLayer()
        {
            if (navMesh == null)
                return;
            
            int agentLayer = gameObject.layer;
            LayerMask obstacleLayers = navMesh.obstacleLayers;
            
            // Проверяем входит ли слой агента в маску препятствий
            if ((obstacleLayers.value & (1 << agentLayer)) != 0)
            {
                string layerName = LayerMask.LayerToName(agentLayer);
                Debug.LogWarning(
                    $"NavMesh3DAgent on '{gameObject.name}': Agent is on layer '{layerName}' which is included in NavMesh3D.obstacleLayers! " +
                    $"This will cause the agent to block NavMesh nodes around itself. " +
                    $"Move the agent to a layer excluded from obstacleLayers, or remove '{layerName}' from the NavMesh3D obstacle mask.",
                    gameObject
                );
            }
        }
        
        private void Update()
        {
            // Обновляем слежение за целью
            UpdateFollow();
            
            if (IsStopped)
            {
                // Плавная остановка при паузе
                if (currentVelocity.sqrMagnitude > 0.001f && acceleration > 0)
                {
                    currentVelocity = Vector3.MoveTowards(currentVelocity, Vector3.zero, acceleration * Time.deltaTime);
                    currentSpeed = currentVelocity.magnitude;
                }
                return;
            }
            
            if (Status == AgentStatus.Moving && HasPath)
            {
                UpdateMovement();
                CheckStuck();
                if (!CheckRebaked())
                    CheckPathBlocked();
            }
        }
        
        /// <summary>
        /// Сетку перезапекли (например, новая пещера): старый путь мог уйти в камень,
        /// поэтому он пересчитывается сразу, независимо от checkPathBlocked.
        /// </summary>
        private bool CheckRebaked()
        {
            if (navMesh == null || pathBakeVersion == navMesh.BakeVersion)
                return false;
            
            pathBakeVersion = navMesh.BakeVersion;
            if (Destination.HasValue)
            {
                if (useAvoidance && NavMesh3DAsyncPathfinder.Instance != null)
                    SetDestinationAsync(Destination.Value, PathPriority.High);
                else
                    SetDestination(Destination.Value);
            }
            return true;
        }
        
        private void OnDestroy()
        {
            // Отменяем все асинхронные запросы при уничтожении
            if (pendingRequestId > 0 && NavMesh3DAsyncPathfinder.Instance != null)
            {
                NavMesh3DAsyncPathfinder.Instance.CancelRequest(pendingRequestId);
            }
            
            // Отписываемся от системы избегания
            if (useAvoidance && NavMesh3DAvoidance.Instance != null)
            {
                NavMesh3DAvoidance.Instance.UnregisterAgent(this);
            }
        }
        
        // ===== Публичные методы =====
        
        /// <summary>
        /// Устанавливает цель для движения.
        /// Агент автоматически найдёт путь и начнёт движение.
        /// </summary>
        /// <param name="destination">Целевая позиция в мировых координатах</param>
        /// <returns>true если путь найден</returns>
        public bool SetDestination(Vector3 destination)
        {
            if (navMesh == null || !navMesh.IsBaked)
            {
                Status = AgentStatus.PathFailed;
                OnPathFailed?.Invoke(PathResult.NotBaked);
                return false;
            }
            
            // Применяем offset чтобы агенты не толпились в одной точке
            Vector3 finalDestination = destination;
            if (useDestinationOffset)
            {
                finalDestination = ApplyDestinationOffset(destination);
            }
            
            Destination = finalDestination;
            
            // Ищем путь
            var request = new PathRequest
            {
                start = cachedTransform.position,
                end = finalDestination,
                allowDiagonal = allowDiagonal,
                maxIterations = maxIterations
            };
            
            var response = navMesh.FindPath(request);
            
            if (response.Success)
            {
                SetPath(response.path);
                OnPathFound?.Invoke();
                return true;
            }
            else
            {
                currentPath = null;
                Status = AgentStatus.PathFailed;
                OnPathFailed?.Invoke(response.result);
                return false;
            }
        }
        
        /// <summary>
        /// Асинхронно устанавливает цель для движения.
        /// Путь ищется в фоне, не блокируя игру.
        /// Используй когда много агентов ищут пути одновременно.
        /// </summary>
        /// <param name="destination">Целевая позиция</param>
        /// <param name="priority">Приоритет запроса</param>
        /// <returns>ID запроса (можно использовать для отмены)</returns>
        public int SetDestinationAsync(Vector3 destination, PathPriority priority = PathPriority.Normal)
        {
            // Применяем offset чтобы агенты не толпились в одной точке
            Vector3 finalDestination = destination;
            if (useDestinationOffset)
            {
                finalDestination = ApplyDestinationOffset(destination);
            }
            
            Destination = finalDestination;
            Status = AgentStatus.CalculatingPath;
            
            // Отменяем предыдущий запрос если был
            if (pendingRequestId > 0)
            {
                NavMesh3DAsyncPathfinder.Instance.CancelRequest(pendingRequestId);
            }
            
            pendingRequestId = NavMesh3DAsyncPathfinder.Instance.RequestPathForAgent(
                this,
                finalDestination,
                OnAsyncPathComplete,
                priority
            );
            
            return pendingRequestId;
        }
        
        /// <summary>
        /// Отменяет текущий асинхронный запрос пути.
        /// </summary>
        public void CancelAsyncRequest()
        {
            if (pendingRequestId > 0)
            {
                NavMesh3DAsyncPathfinder.Instance.CancelRequest(pendingRequestId);
                pendingRequestId = 0;
                
                if (Status == AgentStatus.CalculatingPath)
                {
                    Status = AgentStatus.Idle;
                }
            }
        }
        
        private void OnAsyncPathComplete(PathResponse response)
        {
            pendingRequestId = 0;
            
            if (response.Success)
            {
                SetPath(response.path);
                OnPathFound?.Invoke();
            }
            else
            {
                currentPath = null;
                Status = AgentStatus.PathFailed;
                OnPathFailed?.Invoke(response.result);
            }
        }
        
        /// <summary>
        /// Вычисляет путь к цели БЕЗ автоматического движения.
        /// Используй для кастомной логики передвижения.
        /// </summary>
        /// <param name="destination">Целевая позиция</param>
        /// <param name="path">Найденный путь (или null если не найден)</param>
        /// <returns>Результат поиска</returns>
        public PathResult CalculatePath(Vector3 destination, out List<Vector3> path)
        {
            path = null;
            
            if (navMesh == null || !navMesh.IsBaked)
            {
                return PathResult.NotBaked;
            }
            
            var request = new PathRequest
            {
                start = cachedTransform.position,
                end = destination,
                allowDiagonal = allowDiagonal,
                maxIterations = maxIterations
            };
            
            var response = navMesh.FindPath(request);
            
            if (response.Success)
            {
                path = response.path;
            }
            
            return response.result;
        }
        
        /// <summary>
        /// Асинхронно вычисляет путь БЕЗ автоматического движения.
        /// Результат возвращается через callback.
        /// </summary>
        /// <param name="destination">Целевая точка</param>
        /// <param name="callback">Callback с результатом (PathResult, List путь или null)</param>
        /// <param name="priority">Приоритет запроса</param>
        /// <returns>ID запроса для отмены</returns>
        public int CalculatePathAsync(Vector3 destination, Action<PathResult, List<Vector3>> callback, PathPriority priority = PathPriority.Normal)
        {
            if (navMesh == null || !navMesh.IsBaked)
            {
                callback?.Invoke(PathResult.NotBaked, null);
                return -1;
            }
            
            var asyncPathfinder = NavMesh3DAsyncPathfinder.Instance;
            if (asyncPathfinder == null)
            {
                // Fallback на синхронный
                var result = CalculatePath(destination, out var path);
                callback?.Invoke(result, path);
                return -1;
            }
            
            return asyncPathfinder.RequestPath(
                cachedTransform.position,
                destination,
                response =>
                {
                    callback?.Invoke(response.result, response.Success ? response.path : null);
                },
                priority,
                allowDiagonal,
                maxIterations
            );
        }
        
        /// <summary>
        /// Вычисляет путь между двумя точками БЕЗ автоматического движения.
        /// Статический метод — можно вызывать без агента.
        /// </summary>
        public static PathResult CalculatePath(Vector3 from, Vector3 to, out List<Vector3> path, bool allowDiagonal = true, int maxIterations = 20000)
        {
            path = null;
            
            var navMesh = NavMesh3D.Instance;
            if (navMesh == null || !navMesh.IsBaked)
            {
                return PathResult.NotBaked;
            }
            
            var request = new PathRequest
            {
                start = from,
                end = to,
                allowDiagonal = allowDiagonal,
                maxIterations = maxIterations
            };
            
            var response = navMesh.FindPath(request);
            
            if (response.Success)
            {
                path = response.path;
            }
            
            return response.result;
        }
        
        /// <summary>
        /// Асинхронно вычисляет путь между двумя точками БЕЗ автоматического движения.
        /// Статический метод — можно вызывать без агента.
        /// </summary>
        /// <param name="from">Начальная точка</param>
        /// <param name="to">Конечная точка</param>
        /// <param name="callback">Callback с результатом (PathResult, List путь или null)</param>
        /// <param name="priority">Приоритет запроса</param>
        /// <param name="allowDiagonal">Разрешить диагональное движение</param>
        /// <param name="maxIterations">Максимум итераций A*</param>
        /// <returns>ID запроса для отмены</returns>
        public static int CalculatePathAsync(Vector3 from, Vector3 to, Action<PathResult, List<Vector3>> callback, PathPriority priority = PathPriority.Normal, bool allowDiagonal = true, int maxIterations = 20000)
        {
            var navMesh = NavMesh3D.Instance;
            if (navMesh == null || !navMesh.IsBaked)
            {
                callback?.Invoke(PathResult.NotBaked, null);
                return -1;
            }
            
            var asyncPathfinder = NavMesh3DAsyncPathfinder.Instance;
            if (asyncPathfinder == null)
            {
                // Fallback на синхронный
                var result = CalculatePath(from, to, out var path, allowDiagonal, maxIterations);
                callback?.Invoke(result, path);
                return -1;
            }
            
            return asyncPathfinder.RequestPath(
                from,
                to,
                response =>
                {
                    callback?.Invoke(response.result, response.Success ? response.path : null);
                },
                priority,
                allowDiagonal,
                maxIterations
            );
        }
        
        /// <summary>
        /// Устанавливает готовый путь для следования.
        /// </summary>
        public void SetPath(List<Vector3> path)
        {
            if (path == null || path.Count == 0)
            {
                Stop();
                return;
            }
            
            currentPath = path;
            currentWaypointIndex = 0;
            lastCheckedGridVersion = -1;
            pathBakeVersion = navMesh != null ? navMesh.BakeVersion : -1;
            Status = AgentStatus.Moving;
            
            // Сбрасываем проверку застревания
            lastStuckCheckPosition = cachedTransform.position;
            lastStuckCheckTime = Time.time;
            stuckCount = 0;
        }
        
        /// <summary>
        /// Останавливает агента.
        /// </summary>
        public void Stop()
        {
            // Отменяем асинхронный запрос если есть
            CancelAsyncRequest();
            
            currentPath = null;
            currentWaypointIndex = 0;
            lastCheckedGridVersion = -1;
            Destination = null;
            Status = AgentStatus.Idle;
            
            // НЕ очищаем followTarget — агент может быть просто на паузе
            // Плавная остановка
            // currentVelocity обнулится постепенно через acceleration
        }
        
        /// <summary>
        /// Устанавливает цель для слежения.
        /// Агент будет автоматически следовать за целью, пересчитывая путь
        /// когда цель сместится на followThreshold метров.
        /// </summary>
        /// <param name="target">Transform цели (игрок, объект)</param>
        /// <param name="stopDistance">Остановиться на этом расстоянии от цели (опционально)</param>
        public void SetFollowTarget(Transform target, float? stopDistance = null)
        {
            followTarget = target;
            
            if (stopDistance.HasValue)
            {
                followStopDistance = stopDistance.Value;
            }
            
            // Сбрасываем для немедленного пересчёта
            lastFollowTargetPosition = Vector3.zero;
            lastFollowUpdateTime = 0f;
        }
        
        /// <summary>
        /// Прекращает слежение за целью.
        /// Агент продолжит движение к последней известной позиции цели.
        /// </summary>
        /// <param name="stopMovement">Также остановить движение</param>
        public void ClearFollowTarget(bool stopMovement = false)
        {
            followTarget = null;
            lastFollowTargetPosition = Vector3.zero;
            
            if (stopMovement)
            {
                Stop();
            }
        }
        
        /// <summary>
        /// Телепортирует агента в указанную позицию.
        /// </summary>
        public void Warp(Vector3 position)
        {
            cachedTransform.position = position;
            currentVelocity = Vector3.zero;
            currentSpeed = 0f;
            
            // Пересчитываем путь если был
            if (Destination.HasValue && Status == AgentStatus.Moving)
            {
                SetDestination(Destination.Value);
            }
        }
        
        /// <summary>
        /// Проверяет, достижима ли точка из текущей позиции.
        /// </summary>
        public bool CanReach(Vector3 destination)
        {
            if (navMesh == null || !navMesh.IsBaked)
                return false;
            
            return navMesh.IsReachable(cachedTransform.position, destination);
        }
        
        // ===== Приватные методы =====
        
        private void UpdateMovement()
        {
            if (currentWaypointIndex >= currentPath.Count)
            {
                ReachDestination();
                return;
            }
            
            Vector3 currentPosition = cachedTransform.position;
            Vector3 targetWaypoint = currentPath[currentWaypointIndex];
            Vector3 finalDestination = currentPath[currentPath.Count - 1];
            
            // Расстояния
            float distanceToWaypoint = Vector3.Distance(currentPosition, targetWaypoint);
            float distanceToFinal = Vector3.Distance(currentPosition, finalDestination);
            
            // Простой threshold - фиксированный или на основе шага за кадр
            float threshold = Mathf.Max(waypointThreshold, speed * Time.deltaTime * 1.5f);
            
            // Достигли финальной точки?
            if (distanceToFinal <= threshold)
            {
                ReachDestination();
                return;
            }
            
            // Достигли промежуточной точки - переходим к следующей
            while (distanceToWaypoint <= threshold && currentWaypointIndex < currentPath.Count - 1)
            {
                currentWaypointIndex++;
                targetWaypoint = currentPath[currentWaypointIndex];
                distanceToWaypoint = Vector3.Distance(currentPosition, targetWaypoint);
            }
            
            // Направление к текущей точке
            Vector3 direction = (targetWaypoint - currentPosition).normalized;
            
            // Простая скорость - без ускорения
            currentSpeed = speed;
            currentVelocity = direction * currentSpeed;
            
            // Применяем локальное избегание (опционально)
            if (useAvoidance && NavMesh3DAvoidance.Instance != null)
            {
                Vector3 avoidanceVelocity = NavMesh3DAvoidance.Instance.GetAvoidanceVelocity(this);
                if (avoidanceVelocity.sqrMagnitude > 0.01f)
                {
                    // Ограничиваем избегание
                    float maxAvoidance = currentSpeed * 0.5f;
                    if (avoidanceVelocity.magnitude > maxAvoidance)
                    {
                        avoidanceVelocity = avoidanceVelocity.normalized * maxAvoidance;
                    }
                    currentVelocity += avoidanceVelocity;
                    
                    // Избегание не должно вталкивать агента в стену
                    if (navMesh != null && !navMesh.IsWalkable(currentPosition + currentVelocity * Time.deltaTime))
                    {
                        currentVelocity = direction * currentSpeed;
                    }
                }
            }
            
            // Двигаем агента
            cachedTransform.position += currentVelocity * Time.deltaTime;
            
            // Поворачиваем к цели (не к velocity чтобы избежать дёрганья)
            if (direction.sqrMagnitude > 0.01f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(direction);
                
                if (angularSpeed > 0)
                {
                    cachedTransform.rotation = Quaternion.RotateTowards(
                        cachedTransform.rotation,
                        targetRotation,
                        angularSpeed * Time.deltaTime
                    );
                }
                else
                {
                    cachedTransform.rotation = targetRotation;
                }
            }
        }
        
        private void ReachDestination()
        {
            currentVelocity = Vector3.zero;
            currentSpeed = 0f;
            Status = AgentStatus.ReachedDestination;
            
            OnDestinationReached?.Invoke();
        }
        
        private Vector3 ApplyDestinationOffset(Vector3 destination)
        {
            // Проверяем есть ли другие агенты которые движутся к той же точке
            int neighborsAtDestination = 0;
            float checkRadius = destinationOffsetRadius * 2f;
            
            foreach (var other in ActiveAgents)
            {
                if (other == this)
                    continue;
                
                // Проверяем только движущихся агентов
                if (other.Status != AgentStatus.Moving && other.Status != AgentStatus.CalculatingPath)
                    continue;
                
                // Проверяем идут ли они к той же точке
                if (!other.Destination.HasValue)
                    continue;
                
                float distToSameTarget = Vector3.Distance(other.Destination.Value, destination);
                if (distToSameTarget < checkRadius)
                {
                    neighborsAtDestination++;
                }
            }
            
            // Если никто больше не идёт к этой точке — не смещаем
            if (neighborsAtDestination == 0)
            {
                return destination;
            }
            
            // Есть соседи — применяем offset
            int id = GetInstanceID();
            
            // Генерируем псевдослучайное направление на основе ID
            float angle = (id * 2654435761) % 360;
            float rad = angle * Mathf.Deg2Rad;
            
            // Расстояние от центра
            float distance = ((id * 1597334677) % 1000) / 1000f * destinationOffsetRadius;
            // Минимальное расстояние чтобы не быть в центре
            distance = Mathf.Max(distance, destinationOffsetRadius * 0.3f);
            
            Vector3 offset = new Vector3(
                Mathf.Cos(rad) * distance,
                0,
                Mathf.Sin(rad) * distance
            );
            
            // Смещённая точка может оказаться в стене — тогда идём в исходную
            Vector3 shifted = destination + offset;
            return navMesh != null && navMesh.IsWalkable(shifted) ? shifted : destination;
        }
        
        private void CheckStuck()
        {
            if (!autoRepath)
                return;
            
            if (Time.time - lastStuckCheckTime < stuckCheckInterval)
                return;
            
            float movedDistance = Vector3.Distance(cachedTransform.position, lastStuckCheckPosition);
            
            if (movedDistance < stuckThreshold)
            {
                stuckCount++;
                
                if (stuckCount >= 3) // Застрял 3 проверки подряд
                {
                    // Пересчитываем путь
                    if (Destination.HasValue)
                    {
                        Debug.Log($"NavMesh3DAgent on {gameObject.name}: Stuck detected, recalculating path");
                        SetDestination(Destination.Value);
                    }
                    
                    stuckCount = 0;
                }
            }
            else
            {
                stuckCount = 0;
            }
            
            lastStuckCheckPosition = cachedTransform.position;
            lastStuckCheckTime = Time.time;
        }
        
        private void CheckPathBlocked()
        {
            if (!checkPathBlocked || navMesh == null)
                return;
            
            if (Time.time - lastPathCheckTime < pathCheckInterval)
                return;
            
            lastPathCheckTime = Time.time;
            
            // Сетка не менялась с прошлой проверки — путь по-прежнему свободен
            if (lastCheckedGridVersion == navMesh.GridVersion)
                return;
            lastCheckedGridVersion = navMesh.GridVersion;
            
            // Путь спрямлён, точки редкие: препятствие может встать посреди отрезка,
            // поэтому проверяем видимость по отрезкам, а не только сами точки
            int checkCount = Mathf.Min(pathCheckAhead, currentPath.Count - currentWaypointIndex);
            var pathfinder = navMesh.Pathfinder;
            
            for (int i = 0; i < checkCount; i++)
            {
                int waypointIndex = currentWaypointIndex + i;
                if (waypointIndex >= currentPath.Count)
                    break;
                
                Vector3 from = i == 0 ? cachedTransform.position : currentPath[waypointIndex - 1];
                Vector3 to = currentPath[waypointIndex];
                
                // Если агент сам прижат к стене (узел непроходим), этот отрезок не судим
                if (!navMesh.IsWalkable(from) || pathfinder == null || pathfinder.HasLineOfSight(from, to))
                    continue;
                
                // Путь заблокирован! Пересчитываем
                if (Destination.HasValue)
                {
                    Debug.Log($"NavMesh3DAgent on {gameObject.name}: Path blocked before waypoint {waypointIndex}, recalculating");
                    
                    // Используем асинхронный поиск если включено избегание (много агентов)
                    if (useAvoidance && NavMesh3DAsyncPathfinder.Instance != null)
                    {
                        SetDestinationAsync(Destination.Value, PathPriority.High);
                    }
                    else
                    {
                        SetDestination(Destination.Value);
                    }
                }
                
                return; // Прерываем проверку
            }
        }
        
        private void UpdateFollow()
        {
            // Нет цели для слежения
            if (followTarget == null)
                return;
            
            // Проверяем интервал обновления
            if (Time.time - lastFollowUpdateTime < followUpdateInterval)
                return;
            
            lastFollowUpdateTime = Time.time;
            
            Vector3 targetPosition = followTarget.position;
            float distanceToTarget = Vector3.Distance(cachedTransform.position, targetPosition);
            
            // Если достаточно близко — останавливаемся
            if (distanceToTarget <= followStopDistance)
            {
                // Уже близко, не нужно искать путь
                if (Status == AgentStatus.Moving)
                {
                    Stop();
                    Status = AgentStatus.ReachedDestination;
                    OnDestinationReached?.Invoke();
                }
                
                lastFollowTargetPosition = targetPosition;
                return;
            }
            
            // Проверяем сместилась ли цель достаточно для пересчёта
            float targetMoved = Vector3.Distance(targetPosition, lastFollowTargetPosition);
            
            bool needsRepath = false;
            
            // Первый раз или цель сильно сдвинулась
            if (lastFollowTargetPosition == Vector3.zero || targetMoved >= followThreshold)
            {
                needsRepath = true;
            }
            // Нет пути — нужно проложить
            else if (!HasPath && Status != AgentStatus.CalculatingPath)
            {
                needsRepath = true;
            }
            
            if (needsRepath)
            {
                lastFollowTargetPosition = targetPosition;
                
                // Используем асинхронный поиск чтобы не лагать
                if (NavMesh3DAsyncPathfinder.Instance != null)
                {
                    SetDestinationAsync(targetPosition, PathPriority.Normal);
                }
                else
                {
                    SetDestination(targetPosition);
                }
            }
        }
        
        private float CalculateRemainingDistance()
        {
            if (!HasPath || currentWaypointIndex >= currentPath.Count)
                return 0f;
            
            float distance = Vector3.Distance(cachedTransform.position, currentPath[currentWaypointIndex]);
            
            for (int i = currentWaypointIndex; i < currentPath.Count - 1; i++)
            {
                distance += Vector3.Distance(currentPath[i], currentPath[i + 1]);
            }
            
            return distance;
        }
        
        // ===== Gizmos =====
        
        private void OnDrawGizmos()
        {
            if (!showPath || currentPath == null || currentPath.Count == 0)
                return;
            
            // Рисуем путь
            Gizmos.color = pathColor;
            
            for (int i = 0; i < currentPath.Count - 1; i++)
            {
                Gizmos.DrawLine(currentPath[i], currentPath[i + 1]);
            }
            
            // Рисуем точки пути
            Gizmos.color = Color.yellow;
            foreach (var point in currentPath)
            {
                Gizmos.DrawSphere(point, 0.1f);
            }
            
            // Рисуем текущую целевую точку
            if (currentWaypointIndex < currentPath.Count)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawWireSphere(currentPath[currentWaypointIndex], 0.2f);
            }
            
            // Рисуем финальную цель
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(currentPath[currentPath.Count - 1], 0.3f);
            
            // Рисуем вектор скорости
            if (currentVelocity.sqrMagnitude > 0.01f)
            {
                Gizmos.color = Color.blue;
                Gizmos.DrawRay(transform.position, currentVelocity);
            }
        }
        
        private void OnDrawGizmosSelected()
        {
            // Рисуем радиус остановки
            Gizmos.color = new Color(1, 0, 0, 0.3f);
            if (Destination.HasValue)
            {
                Gizmos.DrawWireSphere(Destination.Value, stoppingDistance);
            }
        }
    }
}
