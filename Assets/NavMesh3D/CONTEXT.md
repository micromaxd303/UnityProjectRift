# NavMesh3D - Контекст для продолжения работы

## Описание проекта

**NavMesh3D** — это система 3D навигации для Unity, поддерживающая полёт (не привязана к поверхности). Разработана для FPS roguelite игры с летающими врагами.

### Ключевые особенности
- 3D сетка навигации (не surface-based как стандартный NavMesh)
- Поддержка больших пространств до 500³ узлов
- HPA* архитектура (чанки 16³ + регионы для быстрой проверки достижимости)
- Job System для быстрого baking
- Асинхронный pathfinding с приоритетами
- RVO-based local avoidance для агентов
- Динамические препятствия (carving)
- Система слежения за целью (follow target)

---

## Архитектура

### Структура файлов
```
NavMesh3D/
├── README.md                 # Полная документация API
├── CONTEXT.md               # Этот файл
├── Runtime/
│   ├── NavMesh3D.cs         # Главный компонент (singleton)
│   ├── NavMesh3DAgent.cs    # Агент навигации
│   ├── NavMesh3DObstacle.cs # Динамические препятствия
│   ├── NavMesh3DAvoidance.cs # Local avoidance (RVO)
│   ├── NavMesh3DAgentController.cs # Тестовый контроллер
│   ├── NavMesh3DPathTest.cs # Визуализация путей
│   ├── Core/
│   │   ├── NavMesh3DChunk.cs   # Чанк 16³
│   │   ├── NavMesh3DRegion.cs  # Регион (связность)
│   │   └── NavMesh3DData.cs    # Структуры данных
│   ├── Baking/
│   │   └── NavMesh3DBaker.cs   # Job System baking
│   └── Pathfinding/
│       ├── NavMesh3DPathfinder.cs      # A* pathfinder
│       ├── NavMesh3DAsyncPathfinder.cs # Async с очередью
│       └── FastPriorityQueue.cs        # Очередь приоритетов
└── Editor/
    └── NavMesh3DEditor.cs    # Custom Inspector
```

### Основные компоненты

1. **NavMesh3D** — главный singleton, управляет сеткой, baking, pathfinding
2. **NavMesh3DAgent** — компонент агента с автоматическим движением
3. **NavMesh3DObstacle** — динамические препятствия (Box/Sphere/Capsule)
4. **NavMesh3DAvoidance** — singleton для local avoidance между агентами
5. **NavMesh3DAsyncPathfinder** — singleton для асинхронного поиска путей

---

## Реализованные фичи

### Baking
- [x] Job System параллельный baking
- [x] Physics.OverlapBox для проверки проходимости
- [x] Автоматическое разбиение на чанки 16³
- [x] Flood-fill для регионов (связность)
- [x] Runtime rebaking

### Pathfinding
- [x] A* алгоритм с 26-connectivity (3D)
- [x] Диагональное движение (опционально)
- [x] Быстрая проверка достижимости через регионы
- [x] Сглаживание пути (line-of-sight)
- [x] Асинхронный поиск с приоритетами (Critical/High/Normal/Low)
- [x] Очередь запросов с лимитом per-frame

### Agent
- [x] Автоматическое движение по пути
- [x] Синхронный и асинхронный SetDestination
- [x] Follow target (слежение за Transform)
- [x] События: OnDestinationReached, OnPathFailed, OnPathFound
- [x] Проверка блокировки пути препятствиями
- [x] Проверка застревания (stuck detection)
- [x] CalculatePath / CalculatePathAsync для кастомного движения
- [x] Destination offset для crowd (агенты не толпятся)
- [x] Проверка слоя агента vs obstacleLayers (warning)

### Local Avoidance
- [x] RVO-based избегание других агентов
- [x] Приоритеты агентов
- [x] Настраиваемые радиусы детекции

### Obstacles
- [x] Box, Sphere, Capsule формы
- [x] Dynamic mode (автообновление при движении)
- [x] Manual mode (ручное Apply/Clear)
- [x] Carving (блокировка узлов)
- [x] Корректная очистка при перемещении

---

## Известные проблемы / TODO

### Возможные улучшения
- [ ] Path smoothing можно улучшить (funnel algorithm)
- [ ] Добавить поддержку разных размеров агентов при baking
- [ ] Добавить визуализацию NavMesh в Editor (Gizmos)
- [ ] Partial rebaking (только изменённые чанки)
- [ ] Сериализация NavMesh в ScriptableObject
- [ ] NavMeshLink аналог для соединения регионов

### Потенциальные баги
- При очень высокой скорости агенты могут немного срезать углы
- Много агентов в одной точке могут "выталкивать" друг друга

---

## Оптимизации 2026-10-31

- **Регионы по узлам, а не по чанкам** (`NavMesh3DRegions`): flood fill внутри чанка + Union-Find по совпадающим узлам на гранях. Раньше соседние чанки склеивались, если у обоих есть хоть какой-то воздух на грани, и IsReachable давал ложное «да» — A* выжигал весь лимит итераций.
- **A* без Dictionary/HashSet**: страницы массивов по чанкам, ключ узла `(чанк << 12) | локальный индекс`, очистка сменой номера поиска.
- **Без срезания углов**: диагональ только если свободны все осевые/рёберные под-шаги. 6-связность регионов теперь совпадает с тем, куда реально проходит A*.
- **Сглаживание пути** по прямой видимости в сетке (Amanatides–Woo, на рёбрах/углах проверяются все соседние узлы). Отключается `PathRequest.skipSmoothing`. `PathRequest.heuristicWeight` (0 = 1) ускоряет поиск ценой оптимальности.
- **Старт/цель в стене** притягиваются к ближайшему проходимому узлу (до `SnapRadius` = 2 узла) вместо BlockedEndpoints.
- **Запекание из поля расстояний** (`BakeFromDistanceField`, `NavMesh3DDistanceFieldBaker`): для воксельных пещер вместо OverlapSphere. Связка с генератором — `CaveGenerator/Navigation/CaveNavMesh3DBaker.cs`.
- Агент: реестр активных агентов вместо `FindObjectsByType`; смещённая цель проверяется на проходимость; избегание не толкает в стену.

---

## Ключевые решения

### Почему HPA* а не просто A*
Для больших сеток (500³ = 125M узлов) чистый A* слишком медленный. HPA* сначала проверяет связность через регионы (O(1)), затем ищет путь только если цель достижима.

### Почему Job System для baking
Physics.OverlapBox на 125M узлов занимает минуты. С Job System + Burst — секунды.

### Почему RVO для avoidance
Простой separation force создаёт "дрожание". RVO предсказывает коллизии и плавно корректирует velocity.

### Упрощённое движение агента
Изначально было ускорение/замедление, но на высокой скорости агенты кружились. Упростили до постоянной скорости с динамическим threshold для waypoints.

---

## Примеры использования

### Базовое использование
```csharp
// Bake
NavMesh3D.Instance.Bake();

// Найти путь
var path = NavMesh3D.Instance.FindPath(start, end);

// Агент
agent.SetDestination(target);
// или асинхронно
agent.SetDestinationAsync(target, PathPriority.High);
```

### Слежение за игроком
```csharp
agent.SetFollowTarget(player.transform, stopDistance: 2f);
agent.OnDestinationReached += Attack;
```

### Кастомное движение
```csharp
agent.CalculatePathAsync(target, (result, path) => {
    if (path != null) {
        myMovement.SetPath(path);
    }
});
```

### Динамическое препятствие
```csharp
// На двери
obstacle.updateMode = ObstacleUpdateMode.Manual;
void OpenDoor() => obstacle.ClearObstacle();
void CloseDoor() => obstacle.ApplyObstacle();
```

---

## Настройки по умолчанию

### NavMesh3D
- gridSize: 100×100×100
- nodeSize: 1.0
- chunkSize: 16

### Agent
- speed: 5
- waypointThreshold: 0.3
- followUpdateInterval: 0.5
- followThreshold: 2.0
- pathCheckInterval: 0.3

### AsyncPathfinder
- maxPathsPerFrame: 5
- maxIterationsPerFrame: 10000

---

## Контакты / История

Проект разрабатывался в серии чатов. Основные этапы:
1. Базовая реализация NavMesh3D + Chunk + Region
2. A* pathfinding + path smoothing
3. Agent с движением
4. Async pathfinding с приоритетами
5. Local avoidance (RVO)
6. Dynamic obstacles
7. Follow target система
8. Багфиксы и оптимизации

Полная документация API находится в README.md
