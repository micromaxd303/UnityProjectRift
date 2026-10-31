using System;
using System.Collections.Generic;
using UnityEngine;

namespace NavMesh3DSystem
{
    /// <summary>
    /// Приоритет запроса пути.
    /// Высокий приоритет обрабатывается первым.
    /// </summary>
    public enum PathPriority
    {
        Low = 0,
        Normal = 1,
        High = 2,
        Critical = 3
    }
    
    /// <summary>
    /// Асинхронный запрос пути.
    /// </summary>
    public class AsyncPathRequest
    {
        public int id;
        public Vector3 start;
        public Vector3 end;
        public PathPriority priority;
        public bool allowDiagonal;
        public int maxIterations;
        
        /// <summary>Callback при завершении</summary>
        public Action<PathResponse> callback;
        
        /// <summary>Время создания запроса</summary>
        public float requestTime;
        
        /// <summary>Агент, запросивший путь (может быть null)</summary>
        public NavMesh3DAgent agent;
    }
    
    /// <summary>
    /// Асинхронный поиск пути с распределением нагрузки по кадрам.
    /// 
    /// ЗАЧЕМ ЭТО НУЖНО:
    /// Если 50 врагов одновременно запросят путь, игра зависнет на секунду.
    /// AsyncPathfinder распределяет запросы по кадрам:
    /// - Кадр 1: обработать 5 запросов
    /// - Кадр 2: обработать следующие 5
    /// - и т.д.
    /// 
    /// ИСПОЛЬЗОВАНИЕ:
    /// 1. AsyncPathfinder создаётся автоматически при первом обращении
    /// 2. Вызови RequestPath() для добавления запроса
    /// 3. Callback вызовется когда путь будет найден
    /// 
    /// ПРИОРИТЕТЫ:
    /// - Critical: Игрок, важные враги — обрабатываются первыми
    /// - High: Близкие враги
    /// - Normal: Обычные враги
    /// - Low: Фоновые NPC
    /// </summary>
    public class NavMesh3DAsyncPathfinder : MonoBehaviour
    {
        // ===== Настройки =====
        
        [Header("Performance")]
        [Tooltip("Максимум запросов обрабатываемых за кадр")]
        [Range(1, 50)]
        public int maxRequestsPerFrame = 5;
        
        [Tooltip("Максимальное время (мс) на обработку за кадр")]
        [Range(1f, 50f)]
        public float maxMillisecondsPerFrame = 2f;
        
        [Tooltip("Максимальный размер очереди (старые запросы отбрасываются)")]
        [Range(10, 1000)]
        public int maxQueueSize = 200;
        
        [Header("Debug")]
        [Tooltip("Показывать статистику в консоли")]
        public bool debugLog = false;
        
        // ===== Singleton =====
        
        private static NavMesh3DAsyncPathfinder instance;
        private static bool applicationQuitting = false;
        
        /// <summary>
        /// Глобальный экземпляр AsyncPathfinder.
        /// Создаётся автоматически при первом обращении.
        /// </summary>
        public static NavMesh3DAsyncPathfinder Instance
        {
            get
            {
                if (applicationQuitting)
                    return null;
                
                if (instance == null)
                {
                    // Ищем существующий
                    instance = FindFirstObjectByType<NavMesh3DAsyncPathfinder>();
                    
                    if (instance == null && !applicationQuitting)
                    {
                        // Создаём новый
                        var go = new GameObject("NavMesh3DAsyncPathfinder");
                        instance = go.AddComponent<NavMesh3DAsyncPathfinder>();
                        DontDestroyOnLoad(go);
                    }
                }
                return instance;
            }
        }
        
        // ===== Публичные свойства =====
        
        /// <summary>Количество запросов в очереди</summary>
        public int QueuedRequests => requestQueues[0].Count + requestQueues[1].Count + 
                                      requestQueues[2].Count + requestQueues[3].Count;
        
        /// <summary>Запросов обработано за последний кадр</summary>
        public int LastFrameProcessed { get; private set; }
        
        /// <summary>Среднее время ожидания (мс)</summary>
        public float AverageWaitTime { get; private set; }
        
        // ===== Приватные поля =====
        
        // Очереди по приоритетам (индекс = приоритет)
        private readonly Queue<AsyncPathRequest>[] requestQueues = new Queue<AsyncPathRequest>[4];
        
        // Счётчик для уникальных ID
        private int nextRequestId = 1;
        
        // Статистика
        private float totalWaitTime;
        private int completedRequests;
        
        // Кэш
        private NavMesh3D navMesh;
        private System.Diagnostics.Stopwatch frameStopwatch;
        
        // ===== Unity Lifecycle =====
        
        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            
            instance = this;
            
            // Инициализируем очереди
            for (int i = 0; i < requestQueues.Length; i++)
            {
                requestQueues[i] = new Queue<AsyncPathRequest>(64);
            }
            
            frameStopwatch = new System.Diagnostics.Stopwatch();
        }
        
        private void Start()
        {
            navMesh = NavMesh3D.Instance;
        }
        
        private void Update()
        {
            ProcessRequests();
        }
        
        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
        }
        
        private void OnApplicationQuit()
        {
            applicationQuitting = true;
        }
        
        // ===== Публичные методы =====
        
        /// <summary>
        /// Добавляет запрос на поиск пути в очередь.
        /// </summary>
        /// <param name="start">Начальная точка</param>
        /// <param name="end">Конечная точка</param>
        /// <param name="callback">Callback при завершении</param>
        /// <param name="priority">Приоритет запроса</param>
        /// <param name="allowDiagonal">Разрешить диагональное движение</param>
        /// <param name="maxIterations">Максимум итераций A*</param>
        /// <returns>ID запроса (можно использовать для отмены)</returns>
        public int RequestPath(
            Vector3 start,
            Vector3 end,
            Action<PathResponse> callback,
            PathPriority priority = PathPriority.Normal,
            bool allowDiagonal = true,
            int maxIterations = 20000)
        {
            return RequestPathInternal(start, end, callback, priority, allowDiagonal, maxIterations, null);
        }
        
        /// <summary>
        /// Добавляет запрос на поиск пути от агента.
        /// </summary>
        internal int RequestPathForAgent(
            NavMesh3DAgent agent,
            Vector3 end,
            Action<PathResponse> callback,
            PathPriority priority = PathPriority.Normal)
        {
            return RequestPathInternal(
                agent.transform.position,
                end,
                callback,
                priority,
                agent.allowDiagonal,
                agent.maxIterations,
                agent
            );
        }
        
        private int RequestPathInternal(
            Vector3 start,
            Vector3 end,
            Action<PathResponse> callback,
            PathPriority priority,
            bool allowDiagonal,
            int maxIterations,
            NavMesh3DAgent agent)
        {
            // Проверяем размер очереди
            if (QueuedRequests >= maxQueueSize)
            {
                // Удаляем самый старый запрос с низким приоритетом
                RemoveOldestLowPriority();
            }
            
            var request = new AsyncPathRequest
            {
                id = nextRequestId++,
                start = start,
                end = end,
                callback = callback,
                priority = priority,
                allowDiagonal = allowDiagonal,
                maxIterations = maxIterations,
                requestTime = Time.time,
                agent = agent
            };
            
            requestQueues[(int)priority].Enqueue(request);
            
            return request.id;
        }
        
        /// <summary>
        /// Отменяет запрос по ID.
        /// Примечание: запрос удаляется при следующей обработке, не мгновенно.
        /// </summary>
        public void CancelRequest(int requestId)
        {
            // Помечаем callback как null — запрос будет пропущен
            foreach (var queue in requestQueues)
            {
                foreach (var request in queue)
                {
                    if (request.id == requestId)
                    {
                        request.callback = null;
                        return;
                    }
                }
            }
        }
        
        /// <summary>
        /// Отменяет все запросы от агента.
        /// </summary>
        public void CancelRequestsForAgent(NavMesh3DAgent agent)
        {
            foreach (var queue in requestQueues)
            {
                foreach (var request in queue)
                {
                    if (request.agent == agent)
                    {
                        request.callback = null;
                    }
                }
            }
        }
        
        /// <summary>
        /// Очищает все запросы.
        /// </summary>
        public void ClearAllRequests()
        {
            foreach (var queue in requestQueues)
            {
                queue.Clear();
            }
        }
        
        // ===== Приватные методы =====
        
        private void ProcessRequests()
        {
            if (navMesh == null || !navMesh.IsBaked)
            {
                navMesh = NavMesh3D.Instance;
                return;
            }
            
            frameStopwatch.Restart();
            LastFrameProcessed = 0;
            
            // Обрабатываем от высокого приоритета к низкому
            for (int priority = (int)PathPriority.Critical; priority >= 0; priority--)
            {
                var queue = requestQueues[priority];
                
                while (queue.Count > 0)
                {
                    // Проверяем лимиты
                    if (LastFrameProcessed >= maxRequestsPerFrame)
                        return;
                    
                    if (frameStopwatch.ElapsedMilliseconds >= maxMillisecondsPerFrame)
                        return;
                    
                    var request = queue.Dequeue();
                    
                    // Пропускаем отменённые
                    if (request.callback == null)
                        continue;
                    
                    // Обновляем позицию если это агент (мог переместиться пока ждал)
                    if (request.agent != null)
                    {
                        request.start = request.agent.transform.position;
                    }
                    
                    // Ищем путь
                    var pathRequest = new PathRequest
                    {
                        start = request.start,
                        end = request.end,
                        allowDiagonal = request.allowDiagonal,
                        maxIterations = request.maxIterations
                    };
                    
                    var response = navMesh.FindPath(pathRequest);
                    
                    // Обновляем статистику
                    float waitTime = (Time.time - request.requestTime) * 1000f;
                    totalWaitTime += waitTime;
                    completedRequests++;
                    AverageWaitTime = totalWaitTime / completedRequests;
                    
                    // Вызываем callback
                    try
                    {
                        request.callback?.Invoke(response);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"NavMesh3DAsyncPathfinder: Callback error - {e}");
                    }
                    
                    LastFrameProcessed++;
                    
                    if (debugLog)
                    {
                        Debug.Log($"AsyncPath: {response.result}, " +
                                 $"iterations={response.iterations}, " +
                                 $"wait={waitTime:F1}ms, " +
                                 $"queue={QueuedRequests}");
                    }
                }
            }
        }
        
        private void RemoveOldestLowPriority()
        {
            // Пробуем удалить из Low, потом Normal
            for (int priority = 0; priority <= 1; priority++)
            {
                var queue = requestQueues[priority];
                if (queue.Count > 0)
                {
                    var removed = queue.Dequeue();
                    
                    // Уведомляем об отмене
                    removed.callback?.Invoke(new PathResponse
                    {
                        result = PathResult.IterationLimit, // Используем как "отменён"
                        path = null,
                        iterations = 0
                    });
                    
                    return;
                }
            }
        }
        
        // ===== Gizmos =====
        
        private void OnGUI()
        {
            if (!debugLog)
                return;
            
            GUILayout.BeginArea(new Rect(10, 10, 300, 100));
            GUILayout.Label($"Async Pathfinder Queue: {QueuedRequests}");
            GUILayout.Label($"Last Frame: {LastFrameProcessed} requests");
            GUILayout.Label($"Avg Wait: {AverageWaitTime:F1}ms");
            GUILayout.EndArea();
        }
    }
}
