using System.Collections.Generic;
using UnityEngine;

namespace NavMesh3DSystem
{
    /// <summary>
    /// Система локального избегания столкновений (Local Avoidance).
    /// 
    /// АЛГОРИТМ:
    /// Упрощённый RVO (Reciprocal Velocity Obstacles):
    /// 1. Каждый агент смотрит на соседей в радиусе
    /// 2. Вычисляет "силу отталкивания" от каждого соседа
    /// 3. Корректирует свою скорость чтобы избежать столкновения
    /// 
    /// ОСОБЕННОСТИ:
    /// - Взаимное избегание (оба агента уступают)
    /// - Приоритеты (важные агенты меньше уступают)
    /// - Оптимизация через пространственное хеширование
    /// 
    /// ИСПОЛЬЗОВАНИЕ:
    /// Система работает автоматически для всех NavMesh3DAgent.
    /// Просто добавь NavMesh3DAvoidance в сцену или он создастся сам.
    /// </summary>
    public class NavMesh3DAvoidance : MonoBehaviour
    {
        // ===== Настройки =====
        
        [Header("Detection")]
        [Tooltip("Радиус обнаружения соседей")]
        [Range(1f, 20f)]
        public float neighborRadius = 5f;
        
        [Tooltip("Максимум соседей для проверки")]
        [Range(1, 20)]
        public int maxNeighbors = 10;
        
        [Header("Avoidance")]
        [Tooltip("Сила избегания (0 = выключено)")]
        [Range(0f, 10f)]
        public float avoidanceStrength = 2f;
        
        [Tooltip("Время предсказания столкновения (сек)")]
        [Range(0.1f, 5f)]
        public float timeHorizon = 2f;
        
        [Tooltip("Радиус агента для расчёта столкновений")]
        [Range(0.1f, 2f)]
        public float agentRadius = 0.5f;
        
        [Header("Performance")]
        [Tooltip("Размер ячейки пространственного хеша")]
        [Range(1f, 10f)]
        public float cellSize = 3f;
        
        [Tooltip("Обновлять каждые N кадров (1 = каждый кадр)")]
        [Range(1, 5)]
        public int updateInterval = 1;
        
        [Header("Debug")]
        public bool showDebug = false;
        
        // ===== Singleton =====
        
        private static NavMesh3DAvoidance instance;
        private static bool applicationQuitting = false;
        
        public static NavMesh3DAvoidance Instance
        {
            get
            {
                if (applicationQuitting)
                    return null;
                
                if (instance == null)
                {
                    instance = FindFirstObjectByType<NavMesh3DAvoidance>();
                    
                    if (instance == null && !applicationQuitting)
                    {
                        var go = new GameObject("NavMesh3DAvoidance");
                        instance = go.AddComponent<NavMesh3DAvoidance>();
                        DontDestroyOnLoad(go);
                    }
                }
                return instance;
            }
        }
        
        // ===== Данные агентов =====
        
        private class AgentData
        {
            public NavMesh3DAgent agent;
            public Transform transform;
            public Vector3 position;
            public Vector3 velocity;
            public float radius;
            public int priority;
            public Vector3 avoidanceVelocity;
        }
        
        private readonly List<AgentData> agents = new List<AgentData>(64);
        private readonly Dictionary<NavMesh3DAgent, AgentData> agentLookup = new Dictionary<NavMesh3DAgent, AgentData>(64);
        
        // Пространственный хеш для быстрого поиска соседей
        private readonly Dictionary<int, List<AgentData>> spatialHash = new Dictionary<int, List<AgentData>>(256);
        private readonly List<AgentData> neighborBuffer = new List<AgentData>(20);
        
        private int frameCounter;
        
        // ===== Unity Lifecycle =====
        
        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
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
        
        private void LateUpdate()
        {
            frameCounter++;
            if (frameCounter % updateInterval != 0)
                return;
            
            if (agents.Count == 0)
                return;
            
            // 1. Обновляем позиции и скорости
            UpdateAgentData();
            
            // 2. Строим пространственный хеш
            BuildSpatialHash();
            
            // 3. Вычисляем избегание для каждого агента
            ComputeAvoidance();
        }
        
        // ===== Публичные методы =====
        
        /// <summary>
        /// Регистрирует агента в системе избегания.
        /// Вызывается автоматически из NavMesh3DAgent.
        /// </summary>
        public void RegisterAgent(NavMesh3DAgent agent, int priority = 0)
        {
            if (agentLookup.ContainsKey(agent))
                return;
            
            var data = new AgentData
            {
                agent = agent,
                transform = agent.transform,
                radius = agentRadius,
                priority = priority
            };
            
            agents.Add(data);
            agentLookup[agent] = data;
        }
        
        /// <summary>
        /// Удаляет агента из системы.
        /// </summary>
        public void UnregisterAgent(NavMesh3DAgent agent)
        {
            if (agentLookup.TryGetValue(agent, out var data))
            {
                agents.Remove(data);
                agentLookup.Remove(agent);
            }
        }
        
        /// <summary>
        /// Устанавливает приоритет агента.
        /// Высокий приоритет = агент меньше уступает другим.
        /// </summary>
        public void SetAgentPriority(NavMesh3DAgent agent, int priority)
        {
            if (agentLookup.TryGetValue(agent, out var data))
            {
                data.priority = priority;
            }
        }
        
        /// <summary>
        /// Устанавливает радиус агента для расчёта столкновений.
        /// </summary>
        public void SetAgentRadius(NavMesh3DAgent agent, float radius)
        {
            if (agentLookup.TryGetValue(agent, out var data))
            {
                data.radius = radius;
            }
        }
        
        /// <summary>
        /// Получает скорректированную скорость для агента.
        /// </summary>
        public Vector3 GetAvoidanceVelocity(NavMesh3DAgent agent)
        {
            if (agentLookup.TryGetValue(agent, out var data))
            {
                return data.avoidanceVelocity;
            }
            return Vector3.zero;
        }
        
        // ===== Приватные методы =====
        
        private void UpdateAgentData()
        {
            for (int i = agents.Count - 1; i >= 0; i--)
            {
                var data = agents[i];
                
                // Удаляем уничтоженных агентов
                if (data.agent == null || data.transform == null)
                {
                    agents.RemoveAt(i);
                    continue;
                }
                
                data.position = data.transform.position;
                data.velocity = data.agent.Velocity;
            }
        }
        
        private void BuildSpatialHash()
        {
            // Очищаем хеш
            foreach (var cell in spatialHash.Values)
            {
                cell.Clear();
            }
            
            // Добавляем агентов в ячейки
            foreach (var data in agents)
            {
                int hash = GetCellHash(data.position);
                
                if (!spatialHash.TryGetValue(hash, out var cell))
                {
                    cell = new List<AgentData>(8);
                    spatialHash[hash] = cell;
                }
                
                cell.Add(data);
            }
        }
        
        private int GetCellHash(Vector3 position)
        {
            int x = Mathf.FloorToInt(position.x / cellSize);
            int y = Mathf.FloorToInt(position.y / cellSize);
            int z = Mathf.FloorToInt(position.z / cellSize);
            
            // Простая хеш-функция
            return x * 73856093 ^ y * 19349663 ^ z * 83492791;
        }
        
        private void GetNeighbors(AgentData agent, List<AgentData> neighbors)
        {
            neighbors.Clear();
            
            float radiusSq = neighborRadius * neighborRadius;
            
            // Проверяем соседние ячейки
            int cx = Mathf.FloorToInt(agent.position.x / cellSize);
            int cy = Mathf.FloorToInt(agent.position.y / cellSize);
            int cz = Mathf.FloorToInt(agent.position.z / cellSize);
            
            int range = Mathf.CeilToInt(neighborRadius / cellSize);
            
            for (int dx = -range; dx <= range; dx++)
            {
                for (int dy = -range; dy <= range; dy++)
                {
                    for (int dz = -range; dz <= range; dz++)
                    {
                        int hash = (cx + dx) * 73856093 ^ (cy + dy) * 19349663 ^ (cz + dz) * 83492791;
                        
                        if (!spatialHash.TryGetValue(hash, out var cell))
                            continue;
                        
                        foreach (var other in cell)
                        {
                            if (other == agent)
                                continue;
                            
                            float distSq = (other.position - agent.position).sqrMagnitude;
                            
                            if (distSq < radiusSq)
                            {
                                neighbors.Add(other);
                                
                                if (neighbors.Count >= maxNeighbors)
                                    return;
                            }
                        }
                    }
                }
            }
        }
        
        private void ComputeAvoidance()
        {
            foreach (var agent in agents)
            {
                agent.avoidanceVelocity = Vector3.zero;
                
                // Пропускаем стоящих агентов
                if (agent.agent.Status != AgentStatus.Moving)
                    continue;
                
                // Пропускаем если агент остановлен
                if (agent.agent.IsStopped)
                    continue;
                
                GetNeighbors(agent, neighborBuffer);
                
                if (neighborBuffer.Count == 0)
                    continue;
                
                Vector3 separation = Vector3.zero;  // Отталкивание от слишком близких
                Vector3 avoidance = Vector3.zero;   // Избегание столкновений
                int separationCount = 0;
                
                foreach (var other in neighborBuffer)
                {
                    Vector3 toOther = other.position - agent.position;
                    float distance = toOther.magnitude;
                    
                    if (distance < 0.001f)
                    {
                        // Агенты в одной точке — толкаем в случайную сторону
                        separation += new Vector3(
                            UnityEngine.Random.Range(-1f, 1f),
                            0,
                            UnityEngine.Random.Range(-1f, 1f)
                        ).normalized * 2f;
                        separationCount++;
                        continue;
                    }
                    
                    Vector3 fromOther = -toOther / distance; // Направление ОТ соседа
                    float combinedRadius = agent.radius + other.radius;
                    
                    // === SEPARATION: Жёсткое отталкивание если слишком близко ===
                    if (distance < combinedRadius * 1.5f)
                    {
                        // Чем ближе, тем сильнее отталкивание
                        float penetration = 1f - (distance / (combinedRadius * 1.5f));
                        penetration = penetration * penetration; // Квадратичный рост
                        
                        separation += fromOther * penetration * 3f;
                        separationCount++;
                    }
                    
                    // === AVOIDANCE: Предсказание столкновения ===
                    // Проверяем только если движемся к соседу
                    float dotToTarget = Vector3.Dot(agent.velocity.normalized, toOther.normalized);
                    if (dotToTarget < 0.2f)
                        continue; // Не движемся к этому соседу
                    
                    // Простая проверка: столкнёмся ли мы за timeHorizon секунд?
                    Vector3 relativeVelocity = agent.velocity - other.velocity;
                    float closingSpeed = Vector3.Dot(relativeVelocity, toOther.normalized);
                    
                    if (closingSpeed <= 0)
                        continue; // Расходимся
                    
                    float timeToClosest = distance / closingSpeed;
                    
                    if (timeToClosest > timeHorizon)
                        continue; // Столкновение слишком далеко
                    
                    // Вычисляем направление избегания — перпендикулярно пути
                    Vector3 perpendicular = Vector3.Cross(Vector3.up, toOther.normalized);
                    
                    // Выбираем сторону на основе текущей позиции
                    // Используем детерминированный выбор чтобы оба агента расходились в разные стороны
                    float side = Mathf.Sign(Vector3.Dot(perpendicular, agent.position - other.position));
                    if (Mathf.Abs(side) < 0.1f)
                    {
                        // Почти на одной линии — используем ID для детерминизма
                        side = agent.agent.GetInstanceID() > other.agent.GetInstanceID() ? 1f : -1f;
                    }
                    
                    perpendicular *= side;
                    
                    // Сила зависит от времени до столкновения
                    float urgency = 1f - (timeToClosest / timeHorizon);
                    
                    avoidance += perpendicular * urgency;
                }
                
                // Комбинируем силы
                Vector3 totalForce = Vector3.zero;
                
                if (separationCount > 0)
                {
                    // Separation имеет высший приоритет
                    totalForce += separation;
                }
                
                if (avoidance.sqrMagnitude > 0.001f)
                {
                    totalForce += avoidance.normalized;
                }
                
                // Применяем силу
                if (totalForce.sqrMagnitude > 0.001f)
                {
                    agent.avoidanceVelocity = totalForce.normalized * avoidanceStrength;
                }
            }
        }
        
        // ===== Gizmos =====
        
        private void OnDrawGizmos()
        {
            if (!showDebug || !Application.isPlaying)
                return;
            
            foreach (var agent in agents)
            {
                // Радиус агента
                Gizmos.color = new Color(0, 1, 0, 0.3f);
                Gizmos.DrawWireSphere(agent.position, agent.radius);
                
                // Радиус обнаружения
                Gizmos.color = new Color(1, 1, 0, 0.1f);
                Gizmos.DrawWireSphere(agent.position, neighborRadius);
                
                // Вектор избегания
                if (agent.avoidanceVelocity.sqrMagnitude > 0.01f)
                {
                    Gizmos.color = Color.red;
                    Gizmos.DrawRay(agent.position, agent.avoidanceVelocity);
                }
            }
        }
    }
}
