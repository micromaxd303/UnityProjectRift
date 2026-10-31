using System.Collections.Generic;
using UnityEngine;

namespace NavMesh3DSystem
{
    /// <summary>
    /// Тестовый компонент для визуализации поиска пути.
    /// 
    /// ИСПОЛЬЗОВАНИЕ:
    /// 1. Создай два пустых GameObject (Start и End)
    /// 2. Добавь этот компонент на любой объект
    /// 3. Назначь Start и End в инспекторе
    /// 4. Запусти игру и нажми Space для поиска пути
    /// 
    /// ВАЖНО: Если Start/End имеют коллайдеры, они будут блокировать
    /// узлы навмеша! Решения:
    /// - Убрать коллайдеры с маркеров Start/End
    /// - Или поставить им слой, исключённый в NavMesh3D.obstacleLayers
    /// - Или использовать пустые объекты без коллайдеров
    /// </summary>
    public class NavMesh3DPathTest : MonoBehaviour
    {
        [Header("Endpoints")]
        [Tooltip("Начальная точка пути. Убедись что у объекта нет коллайдера или он на исключённом слое!")]
        public Transform startPoint;
        
        [Tooltip("Конечная точка пути. Убедись что у объекта нет коллайдера или он на исключённом слое!")]
        public Transform endPoint;
        
        [Header("Settings")]
        [Tooltip("Клавиша для поиска пути")]
        public KeyCode findPathKey = KeyCode.Space;
        
        [Tooltip("Автоматически искать путь каждый кадр")]
        public bool autoUpdate = false;
        
        [Tooltip("Разрешить диагональное движение")]
        public bool allowDiagonal = true;
        
        [Tooltip("Максимум итераций A*")]
        public int maxIterations = 20000;
        
        [Header("Visualization")]
        public Color pathColor = Color.green;
        public Color noPathColor = Color.red;
        public float pathWidth = 0.1f;
        public float nodeSize = 0.2f;
        
        [Header("Debug Info")]
        [SerializeField] private PathResult lastResult;
        [SerializeField] private int lastIterations;
        [SerializeField] private float lastPathLength;
        [SerializeField] private int lastPathNodes;
        [SerializeField] private float lastSearchTime;
        
        // Текущий путь
        private List<Vector3> currentPath;
        private bool hasPath;
        
        private void Update()
        {
            if (Input.GetKeyDown(findPathKey) || autoUpdate)
            {
                FindPath();
            }
        }
        
        private void FindPath()
        {
            if (startPoint == null || endPoint == null)
            {
                Debug.LogWarning("NavMesh3DPathTest: Start or End point not assigned!");
                return;
            }
            
            var navMesh = NavMesh3D.Instance;
            if (navMesh == null || !navMesh.IsBaked)
            {
                Debug.LogWarning("NavMesh3DPathTest: NavMesh3D not found or not baked!");
                return;
            }
            
            // Засекаем время
            var startTime = System.DateTime.Now;
            
            // Создаём запрос
            var request = new PathRequest
            {
                start = startPoint.position,
                end = endPoint.position,
                allowDiagonal = allowDiagonal,
                maxIterations = maxIterations
            };
            
            // Ищем путь
            var response = navMesh.FindPath(request);
            
            // Сохраняем результаты
            lastSearchTime = (float)(System.DateTime.Now - startTime).TotalMilliseconds;
            lastResult = response.result;
            lastIterations = response.iterations;
            lastPathLength = response.pathLength;
            
            if (response.Success)
            {
                currentPath = response.path;
                lastPathNodes = currentPath.Count;
                hasPath = true;
                
                Debug.Log($"Path found! Nodes: {lastPathNodes}, Length: {lastPathLength:F2}m, " +
                         $"Iterations: {lastIterations}, Time: {lastSearchTime:F2}ms");
            }
            else
            {
                currentPath = null;
                lastPathNodes = 0;
                hasPath = false;
                
                Debug.Log($"Path not found: {lastResult}, Iterations: {lastIterations}, " +
                         $"Time: {lastSearchTime:F2}ms");
            }
        }
        
        private void OnDrawGizmos()
        {
            // Рисуем маркеры старта и конца
            if (startPoint != null)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawWireSphere(startPoint.position, nodeSize * 1.5f);
                Gizmos.DrawLine(startPoint.position, startPoint.position + Vector3.up * 2);
            }
            
            if (endPoint != null)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawWireSphere(endPoint.position, nodeSize * 1.5f);
                Gizmos.DrawLine(endPoint.position, endPoint.position + Vector3.up * 2);
            }
            
            // Рисуем путь
            if (currentPath != null && currentPath.Count > 0)
            {
                Gizmos.color = hasPath ? pathColor : noPathColor;
                
                // Линии между точками
                for (int i = 0; i < currentPath.Count - 1; i++)
                {
                    Gizmos.DrawLine(currentPath[i], currentPath[i + 1]);
                }
                
                // Узлы пути
                Gizmos.color = Color.yellow;
                foreach (var point in currentPath)
                {
                    Gizmos.DrawSphere(point, nodeSize);
                }
            }
            
            // Рисуем линию если пути нет
            if (!hasPath && startPoint != null && endPoint != null)
            {
                Gizmos.color = noPathColor;
                // Пунктирная линия (приблизительно)
                Vector3 dir = (endPoint.position - startPoint.position);
                float dist = dir.magnitude;
                dir.Normalize();
                
                for (float t = 0; t < dist; t += 0.5f)
                {
                    Vector3 p1 = startPoint.position + dir * t;
                    Vector3 p2 = startPoint.position + dir * Mathf.Min(t + 0.25f, dist);
                    Gizmos.DrawLine(p1, p2);
                }
            }
        }
    }
}
