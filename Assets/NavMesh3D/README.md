# NavMesh3D — Документация

## Обзор

NavMesh3D — система 3D навигации для Unity, разработанная для летающих врагов в шутерах. Поддерживает навигационные сетки размером до 500³ метров с использованием Job System для оптимизации.

### Ключевые особенности

- ✅ Полностью 3D навигация (не только по поверхности)
- ✅ Job System для быстрого запекания
- ✅ A* поиск пути с проверкой регионов
- ✅ Асинхронный поиск пути (распределение по кадрам)
- ✅ Local Avoidance (агенты обходят друг друга)
- ✅ Динамические препятствия (двери, враги)
- ✅ Destination Offset (агенты не толпятся в одной точке)

---

## Архитектура

```
NavMesh3DSystem/
├── Runtime/
│   ├── NavMesh3D.cs              # Главный компонент
│   ├── NavMesh3DAgent.cs         # Агент (враг/NPC)
│   ├── NavMesh3DAgentController.cs # Тестовый контроллер мышью
│   ├── NavMesh3DAvoidance.cs     # Система избегания столкновений
│   ├── NavMesh3DObstacle.cs      # Динамическое препятствие
│   ├── NavMesh3DPathTest.cs      # Тестовый компонент для пути
│   ├── Core/
│   │   ├── NavMesh3DChunk.cs     # Чанк (16³ узлов)
│   │   ├── NavMesh3DData.cs      # Структуры данных
│   │   └── NavMesh3DRegion.cs    # Система регионов
│   ├── Baking/
│   │   └── NavMesh3DBaker.cs     # Job System запекание
│   └── Pathfinding/
│       ├── NavMesh3DPathfinder.cs      # A* алгоритм
│       ├── NavMesh3DAsyncPathfinder.cs # Асинхронный поиск
│       └── FastPriorityQueue.cs        # Очередь с приоритетом
└── Editor/
    └── NavMesh3DEditor.cs        # Кастомный инспектор
```

---

## Компоненты

### NavMesh3D

**Главный компонент навигационной сетки. Singleton.**

#### Настройки (Inspector)

| Параметр | Тип | Описание | По умолчанию |
|----------|-----|----------|--------------|
| `size` | Vector3 | Размер навигационного пространства | (50, 50, 50) |
| `cellSize` | float | Размер одной ячейки (меньше = точнее) | 0.5 |
| `agentRadius` | float | Радиус агента (отступ от препятствий) | 0.5 |
| `obstacleLayers` | LayerMask | Слои препятствий | Everything |
| `bakeOnStart` | bool | Запекать при старте игры | true |
| `showGrid` | bool | Показывать сетку узлов | false |
| `showChunks` | bool | Показывать границы чанков | true |
| `showRegions` | bool | Показывать регионы цветом | false |
| `gridDrawRadius` | float | Радиус отрисовки сетки | 10 |

#### Публичные свойства

```csharp
static NavMesh3D Instance           // Синглтон
bool IsBaked                        // Навмеш готов к использованию
int3 ChunkCount                     // Количество чанков по осям
int TotalChunkCount                 // Общее количество чанков
int TotalNodeCount                  // Общее количество узлов
int RegionCount                     // Количество регионов
NavMesh3DPathfinder Pathfinder      // Доступ к поисковику
NavMesh3DBounds Bounds              // Границы навмеша
```

#### Публичные методы

```csharp
// Запекание
void Bake()                         // Запечь навмеш
void Clear()                        // Очистить навмеш

// Поиск пути
List<Vector3> FindPath(Vector3 from, Vector3 to)              // Простой API
PathResponse FindPathDetailed(Vector3 from, Vector3 to)       // С деталями
PathResponse FindPath(PathRequest request)                    // Полный контроль

// Проверки
bool IsReachable(Vector3 from, Vector3 to)    // Проверка достижимости O(1)
bool IsWalkable(Vector3 worldPosition)        // Проверка проходимости точки
Vector3 GetNearestWalkable(Vector3 pos, float maxDistance = 10f)  // Ближайшая проходимая

// Координаты
GlobalNodeCoord WorldToNode(Vector3 worldPosition)   // Мир → узел
Vector3 NodeToWorld(GlobalNodeCoord nodeCoord)       // Узел → мир
NavMesh3DChunk GetChunkAt(ChunkCoord coord)          // Получить чанк
```

#### Пример использования

```csharp
// Получить навмеш
var navMesh = NavMesh3D.Instance;

// Проверить достижимость (быстро)
if (navMesh.IsReachable(enemy.position, player.position))
{
    // Путь существует
}

// Найти путь
var path = navMesh.FindPath(enemy.position, player.position);
if (path != null)
{
    foreach (var point in path)
    {
        // Двигаться по точкам
    }
}
```

---

### NavMesh3DAgent

**Компонент агента для автоматической навигации.**

#### Настройки (Inspector)

| Группа | Параметр | Описание | По умолчанию |
|--------|----------|----------|--------------|
| **Movement** | `speed` | Скорость движения (м/с) | 5 |
| | `angularSpeed` | Скорость поворота (°/с), 0 = мгновенно | 360 |
| | `acceleration` | Ускорение (м/с²), 0 = мгновенно | 20 |
| | `waypointThreshold` | Расстояние до точки для перехода | 0.3 |
| | `stoppingDistance` | Дистанция замедления | 0.5 |
| **Pathfinding** | `autoRepath` | Пересчёт пути при застревании | true |
| | `stuckCheckInterval` | Интервал проверки застревания | 0.5 |
| | `stuckThreshold` | Порог застревания | 0.1 |
| | `checkPathBlocked` | Проверять блокировку пути препятствиями | true |
| | `pathCheckInterval` | Интервал проверки блокировки | 0.3 |
| | `pathCheckAhead` | Сколько точек пути проверять | 3 |
| | `allowDiagonal` | Диагональное движение | true |
| | `maxIterations` | Лимит итераций A* | 20000 |
| **Avoidance** | `useAvoidance` | Локальное избегание | true |
| | `avoidancePriority` | Приоритет (выше = меньше уступает) | 0 |
| | `radius` | Радиус агента для столкновений | 0.5 |
| **Follow** | `followTarget` | Цель для автоматического слежения | null |
| | `followUpdateInterval` | Интервал обновления пути при слежении | 0.5 |
| | `followThreshold` | Мин. смещение цели для пересчёта пути | 2 |
| | `followStopDistance` | Остановиться на этом расстоянии от цели | 1 |
| **Crowd** | `useDestinationOffset` | Смещение цели (против толпы) | true |
| | `destinationOffsetRadius` | Радиус смещения | 1.5 |
| **Debug** | `showPath` | Показывать путь | true |
| | `pathColor` | Цвет пути | Cyan |

#### Публичные свойства

```csharp
AgentStatus Status                  // Текущий статус агента
Vector3? Destination                // Целевая точка (null если нет)
bool HasPath                        // Есть ли активный путь
float CurrentSpeed                  // Текущая скорость
Vector3 Velocity                    // Текущая скорость (вектор)
float RemainingDistance             // Оставшееся расстояние
IReadOnlyList<Vector3> Path         // Текущий путь (только чтение)
int CurrentWaypointIndex            // Индекс текущей точки
bool IsStopped                      // Пауза (путь сохраняется)
bool IsFollowing                    // Следит ли за целью
float DistanceToFollowTarget        // Расстояние до цели слежения
```

#### Публичные методы

```csharp
// Синхронный поиск (блокирует)
bool SetDestination(Vector3 destination)

// Асинхронный поиск (не блокирует)
int SetDestinationAsync(Vector3 destination, PathPriority priority = Normal)
void CancelAsyncRequest()

// Слежение за целью
void SetFollowTarget(Transform target, float? stopDistance = null)
void ClearFollowTarget(bool stopMovement = false)

// Вычисление пути без движения (синхронно)
PathResult CalculatePath(Vector3 destination, out List<Vector3> path)
static PathResult CalculatePath(Vector3 from, Vector3 to, out List<Vector3> path, ...)

// Вычисление пути без движения (асинхронно)
int CalculatePathAsync(Vector3 destination, Action<PathResult, List<Vector3>> callback, PathPriority priority = Normal)
static int CalculatePathAsync(Vector3 from, Vector3 to, Action<PathResult, List<Vector3>> callback, ...)

// Управление путём
void SetPath(List<Vector3> path)    // Установить готовый путь
void Stop()                         // Остановить и очистить путь
void Warp(Vector3 position)         // Телепорт

// Проверки
bool CanReach(Vector3 destination)  // Достижима ли точка
```

#### События

```csharp
event Action OnDestinationReached      // Достиг цели
event Action<PathResult> OnPathFailed  // Путь не найден
event Action OnPathFound               // Путь найден
```

#### Статусы агента (AgentStatus)

| Статус | Описание |
|--------|----------|
| `Idle` | Стоит на месте |
| `Moving` | Движется к цели |
| `ReachedDestination` | Достиг цели |
| `PathFailed` | Путь не найден |
| `CalculatingPath` | Ожидает асинхронный поиск |

#### Пример: Слежение за игроком

```csharp
public class FlyingEnemy : MonoBehaviour
{
    NavMesh3DAgent agent;
    
    void Start()
    {
        agent = GetComponent<NavMesh3DAgent>();
        
        // Начать следовать за игроком
        var player = GameObject.FindWithTag("Player").transform;
        agent.SetFollowTarget(player, stopDistance: 2f);
        
        // Событие когда агент достиг цели (на расстоянии stopDistance)
        agent.OnDestinationReached += Attack;
    }
    
    void Attack()
    {
        if (agent.DistanceToFollowTarget <= 2f)
        {
            // Атака!
        }
    }
    
    void OnDisable()
    {
        agent.ClearFollowTarget(stopMovement: true);
    }
}
```

#### Пример: Одноразовое движение к точке

```csharp
public class Enemy : MonoBehaviour
{
    NavMesh3DAgent agent;
    Transform player;
    
    void Start()
    {
        agent = GetComponent<NavMesh3DAgent>();
        agent.OnDestinationReached += OnReached;
        agent.OnPathFailed += OnFailed;
    }
    
    void Update()
    {
        // Асинхронный поиск (не лагает)
        if (agent.Status == AgentStatus.Idle)
        {
            agent.SetDestinationAsync(player.position, PathPriority.Normal);
        }
    }
    
    void OnReached()
    {
        // Атака!
    }
    
    void OnFailed(PathResult result)
    {
        Debug.Log($"Не могу добраться: {result}");
    }
}
```

---

### NavMesh3DAvoidance

**Система локального избегания столкновений. Singleton, создаётся автоматически.**

#### Настройки (Inspector)

| Группа | Параметр | Описание | По умолчанию |
|--------|----------|----------|--------------|
| **Detection** | `neighborRadius` | Радиус обнаружения соседей | 5 |
| | `maxNeighbors` | Максимум соседей для проверки | 10 |
| **Avoidance** | `avoidanceStrength` | Сила избегания | 2 |
| | `timeHorizon` | Время предсказания столкновения | 2 |
| | `agentRadius` | Радиус агента по умолчанию | 0.5 |
| **Performance** | `cellSize` | Размер ячейки хеша | 3 |
| | `updateInterval` | Обновлять каждые N кадров | 1 |
| **Debug** | `showDebug` | Показывать визуализацию | false |

#### Публичные методы

```csharp
void RegisterAgent(NavMesh3DAgent agent, int priority = 0)  // Регистрация
void UnregisterAgent(NavMesh3DAgent agent)                  // Удаление
void SetAgentPriority(NavMesh3DAgent agent, int priority)   // Изменить приоритет
void SetAgentRadius(NavMesh3DAgent agent, float radius)     // Изменить радиус
Vector3 GetAvoidanceVelocity(NavMesh3DAgent agent)          // Получить вектор избегания
```

#### Алгоритм

1. Каждый кадр обновляет позиции всех агентов
2. Строит пространственный хеш для быстрого поиска соседей
3. Для каждого движущегося агента:
   - **Separation**: Отталкивание от слишком близких агентов
   - **Avoidance**: Предсказание столкновения и обход
4. Агенты применяют коррекцию скорости в `UpdateMovement()`

---

### NavMesh3DObstacle

**Компонент динамического препятствия для дверей, врагов и т.д.**

#### Настройки (Inspector)

| Группа | Параметр | Описание | По умолчанию |
|--------|----------|----------|--------------|
| **Obstacle** | `shape` | Форма (Box/Sphere/Capsule) | Box |
| | `size` | Размер для Box | (1,1,1) |
| | `radius` | Радиус для Sphere/Capsule | 1 |
| | `useColliderSize` | Брать размер из коллайдера | true |
| **Dynamic** | `isDynamic` | Автообновление | false |
| | `updateInterval` | Интервал обновления (сек) | 0.5 |
| | `updateOnlyWhenMoving` | Обновлять только при движении | true |
| | `movementThreshold` | Порог движения | 0.1 |
| | `clearOnStart` | Очистить область при старте | true |
| **Carving** | `carving` | Активно ли препятствие | true |
| | `carvingPadding` | Дополнительный отступ | 0.1 |
| **Debug** | `showBounds` | Показывать границы | true |

#### Публичные свойства

```csharp
Bounds WorldBounds              // Границы в мировых координатах
int AffectedChunksCount         // Количество затронутых чанков
float LastUpdateTime            // Время последнего обновления
```

#### Публичные методы

```csharp
void UpdateObstacle()           // Ручное обновление
void SetCarving(bool enabled)   // Включить/выключить блокировку
```

#### Примеры использования

**Дверь:**
```csharp
public class Door : MonoBehaviour
{
    NavMesh3DObstacle obstacle;
    
    void Start()
    {
        obstacle = GetComponent<NavMesh3DObstacle>();
        obstacle.isDynamic = false;  // Обновляется только вручную
    }
    
    public void Open()
    {
        // Анимация открытия...
        obstacle.SetCarving(false);  // Путь через дверь открыт
    }
    
    public void Close()
    {
        // Анимация закрытия...
        obstacle.SetCarving(true);   // Путь заблокирован
    }
}
```

**Наземный враг (для обхода летающими):**
```csharp
// В Inspector:
// isDynamic = true
// updateInterval = 0.5
// updateOnlyWhenMoving = true
// clearOnStart = true
```

---

### NavMesh3DAsyncPathfinder

**Асинхронный поиск пути с распределением по кадрам. Singleton, создаётся автоматически.**

#### Настройки (Inspector)

| Параметр | Описание | По умолчанию |
|----------|----------|--------------|
| `maxRequestsPerFrame` | Макс. запросов за кадр | 5 |
| `maxMillisecondsPerFrame` | Макс. время (мс) за кадр | 5 |
| `maxQueueSize` | Макс. размер очереди | 200 |
| `debugLog` | Показывать статистику | false |

#### Публичные свойства

```csharp
int QueuedRequests              // Количество запросов в очереди
int LastFrameProcessed          // Обработано за последний кадр
float AverageWaitTime           // Среднее время ожидания (мс)
```

#### Публичные методы

```csharp
int RequestPath(Vector3 start, Vector3 end, Action<PathResponse> callback, 
                PathPriority priority = Normal, bool allowDiagonal = true, 
                int maxIterations = 20000)
                
void CancelRequest(int requestId)
void CancelRequestsForAgent(NavMesh3DAgent agent)
void ClearAllRequests()
```

#### Приоритеты (PathPriority)

| Приоритет | Когда использовать |
|-----------|-------------------|
| `Critical` | Игрок, босс — обрабатывается первым |
| `High` | Близкие враги |
| `Normal` | Обычные враги (по умолчанию) |
| `Low` | Фоновые NPC |

---

### NavMesh3DAgentController

**Тестовый контроллер для управления агентом мышью.**

#### Настройки

| Параметр | Описание | По умолчанию |
|----------|----------|--------------|
| `mouseButton` | Кнопка мыши (0=левая, 1=правая) | 1 |
| `groundLayers` | Слои для raycast | Everything |
| `useAsync` | Использовать асинхронный поиск | true |
| `priority` | Приоритет запроса | Normal |
| `showTargetMarker` | Показывать маркер цели | true |
| `targetMarkerPrefab` | Префаб маркера | null |
| `markerDuration` | Время показа маркера | 1 |

---

### NavMesh3DPathTest

**Тестовый компонент для визуализации поиска пути.**

#### Использование

1. Создай два пустых GameObject (Start и End)
2. Добавь компонент на любой объект
3. Назначь Start и End в инспекторе
4. Запусти игру и нажми Space

**Важно:** Если Start/End имеют коллайдеры, они заблокируют узлы навмеша!

---

## Структуры данных

### PathRequest

```csharp
struct PathRequest
{
    Vector3 start;              // Начальная точка
    Vector3 end;                // Конечная точка
    bool allowDiagonal;         // Диагональное движение
    int maxIterations;          // Лимит итераций A*
    
    static PathRequest Create(Vector3 from, Vector3 to);
}
```

### PathResponse

```csharp
struct PathResponse
{
    PathResult result;          // Результат поиска
    List<Vector3> path;         // Найденный путь
    int iterations;             // Затраченные итерации
    float pathLength;           // Длина пути (метры)
    
    bool Success;               // result == PathResult.Found
}
```

### PathResult

```csharp
enum PathResult
{
    Found,              // Путь найден
    NotFound,           // Путь не существует
    StartBlocked,       // Начальная точка заблокирована
    EndBlocked,         // Конечная точка заблокирована
    SameRegion,         // Точки в разных регионах
    IterationLimit,     // Превышен лимит итераций
    NotBaked            // Навмеш не запечён
}
```

---

## Оптимизация

### Память

- Один чанк 16³ = 4096 узлов = ~512 байт (1 бит на узел)
- Карта 100×100×100 м с cellSize=0.5 = ~15.6 MB

### Производительность

| Операция | Сложность | Примечание |
|----------|-----------|------------|
| IsReachable | O(1) | Проверка через регионы |
| IsWalkable | O(1) | Прямой доступ к чанку |
| FindPath | O(n log n) | A* с приоритетной очередью |
| Bake | O(n) | Job System, параллельно |

### Рекомендации

1. **cellSize** — баланс точности и памяти. Для большинства игр 0.5-1.0 оптимально
2. **maxIterations** — ограничивает время поиска длинных путей
3. **Async pathfinding** — используй для 10+ агентов
4. **updateInterval в Obstacle** — для врагов 0.3-0.5 сек достаточно

---

## Отладка

### Визуализация в Scene View

- **Show Grid** — показывает узлы (зелёные = проходимые, красные = заблокированные)
- **Show Chunks** — границы чанков с процентом заполнения
- **Show Regions** — чанки раскрашены по регионам

### Логи

```
NavMesh3D: Baked in 0.45s. Chunks: 125, Nodes: 512,000 (walkable: 450,000, blocked: 62,000), Regions: 1
```

### Частые проблемы

| Проблема | Причина | Решение |
|----------|---------|---------|
| Путь не найден | Точки в разных регионах | Проверь IsReachable() |
| Агент застревает | Слишком маленький waypointThreshold | Увеличь до 0.3-0.5 |
| Агенты проходят сквозь | avoidanceStrength слишком низкий | Увеличь до 3-4 |
| Лаги при поиске | Много агентов ищут одновременно | Используй SetDestinationAsync |
| Препятствие не работает | clearOnStart = false для движущегося | Установи clearOnStart = true |

---

## Быстрый старт

### 1. Настройка навмеша

1. Создай пустой GameObject
2. Добавь компонент `NavMesh3D`
3. Настрой размер (`size`) и другие параметры
4. Нажми "Bake" в инспекторе

### 2. Добавление агента

1. Создай/выбери объект врага
2. Добавь компонент `NavMesh3DAgent`
3. Настрой скорость и другие параметры

### 3. Управление агентом

```csharp
var agent = GetComponent<NavMesh3DAgent>();

// Двигаться к игроку
agent.SetDestinationAsync(player.position);

// Остановить
agent.Stop();

// Проверить статус
if (agent.Status == AgentStatus.ReachedDestination)
{
    // Атака!
}
```

### 4. Динамические препятствия

1. Добавь `NavMesh3DObstacle` на объект
2. Для дверей: `isDynamic = false`, вызывай `SetCarving()` вручную
3. Для врагов: `isDynamic = true`, `updateInterval = 0.5`

---

## Версия

**1.0.0** — Базовая функциональность
- 3D навигация с чанками
- A* поиск пути
- Асинхронный поиск
- Local Avoidance
- Динамические препятствия
