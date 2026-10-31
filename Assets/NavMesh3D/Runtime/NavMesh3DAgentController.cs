using UnityEngine;

namespace NavMesh3DSystem
{
    /// <summary>
    /// Простой контроллер для тестирования NavMesh3DAgent.
    /// Кликни мышью — агент пойдёт к точке.
    /// 
    /// ИСПОЛЬЗОВАНИЕ:
    /// 1. Добавь NavMesh3DAgent на объект
    /// 2. Добавь этот скрипт на тот же объект
    /// 3. Запусти игру и кликай мышью
    /// </summary>
    [RequireComponent(typeof(NavMesh3DAgent))]
    public class NavMesh3DAgentController : MonoBehaviour
    {
        [Header("Input")]
        [Tooltip("Кнопка мыши для движения (0=левая, 1=правая)")]
        public int mouseButton = 1;
        
        [Tooltip("Слои для raycast (куда можно кликать)")]
        public LayerMask groundLayers = ~0;
        
        [Header("Pathfinding")]
        [Tooltip("Использовать асинхронный поиск пути (для множества агентов одновременно)")]
        public bool useAsync = false;
        
        [Tooltip("Приоритет запроса пути (только для async)")]
        public PathPriority priority = PathPriority.Normal;
        
        [Header("Visual Feedback")]
        [Tooltip("Показывать маркер цели")]
        public bool showTargetMarker = true;
        
        [Tooltip("Префаб маркера (если null — рисуется Gizmo)")]
        public GameObject targetMarkerPrefab;
        
        [Tooltip("Время отображения маркера")]
        public float markerDuration = 1f;
        
        private NavMesh3DAgent agent;
        private Vector3? clickedPosition;
        private float clickTime;
        private GameObject spawnedMarker;
        
        private void Awake()
        {
            agent = GetComponent<NavMesh3DAgent>();
        }
        
        private void Start()
        {
            // Подписываемся на события агента
            agent.OnDestinationReached += OnReached;
            agent.OnPathFailed += OnFailed;
            agent.OnPathFound += OnPathFound;
        }
        
        private void OnDestroy()
        {
            if (agent != null)
            {
                agent.OnDestinationReached -= OnReached;
                agent.OnPathFailed -= OnFailed;
                agent.OnPathFound -= OnPathFound;
            }
            
            if (spawnedMarker != null)
            {
                Destroy(spawnedMarker);
            }
        }
        
        private void Update()
        {
            HandleInput();
            UpdateMarker();
        }
        
        private void HandleInput()
        {
            if (Input.GetMouseButtonDown(mouseButton))
            {
                Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
                
                if (Physics.Raycast(ray, out RaycastHit hit, 1000f, groundLayers))
                {
                    clickedPosition = hit.point;
                    clickTime = Time.time;
                    
                    if (useAsync)
                    {
                        // Асинхронный поиск — не блокирует игру
                        agent.SetDestinationAsync(hit.point, priority);
                        ShowMarker(hit.point);
                    }
                    else
                    {
                        // Синхронный поиск — может вызвать лаг при сложном пути
                        bool success = agent.SetDestination(hit.point);
                        if (success)
                        {
                            ShowMarker(hit.point);
                        }
                    }
                }
            }
        }
        
        private void ShowMarker(Vector3 position)
        {
            if (!showTargetMarker)
                return;
            
            if (targetMarkerPrefab != null)
            {
                if (spawnedMarker != null)
                {
                    Destroy(spawnedMarker);
                }
                
                spawnedMarker = Instantiate(targetMarkerPrefab, position, Quaternion.identity);
            }
        }
        
        private void UpdateMarker()
        {
            // Удаляем маркер после таймаута
            if (spawnedMarker != null && Time.time - clickTime > markerDuration)
            {
                Destroy(spawnedMarker);
                spawnedMarker = null;
            }
            
            // Очищаем Gizmo позицию
            if (clickedPosition.HasValue && Time.time - clickTime > markerDuration)
            {
                clickedPosition = null;
            }
        }
        
        private void OnReached()
        {
            Debug.Log($"{gameObject.name}: Destination reached!");
        }
        
        private void OnFailed(PathResult result)
        {
            Debug.LogWarning($"{gameObject.name}: Path failed - {result}");
        }
        
        private void OnPathFound()
        {
            Debug.Log($"{gameObject.name}: Path found, {agent.Path.Count} waypoints");
        }
        
        private void OnDrawGizmos()
        {
            // Рисуем маркер цели (если нет префаба)
            if (showTargetMarker && clickedPosition.HasValue && targetMarkerPrefab == null)
            {
                float alpha = 1f - (Time.time - clickTime) / markerDuration;
                alpha = Mathf.Clamp01(alpha);
                
                Gizmos.color = new Color(0, 1, 0, alpha);
                Gizmos.DrawWireSphere(clickedPosition.Value, 0.5f);
                Gizmos.DrawLine(clickedPosition.Value, clickedPosition.Value + Vector3.up * 2f);
            }
        }
    }
}
