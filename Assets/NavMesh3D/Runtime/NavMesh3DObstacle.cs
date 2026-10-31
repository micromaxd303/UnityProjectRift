using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace NavMesh3DSystem
{
    /// <summary>
    /// Динамическое препятствие для NavMesh3D.
    ///
    /// ИСПОЛЬЗОВАНИЕ:
    /// 1. Добавь на объект с коллайдером (дверь, враг, стена)
    /// 2. Настрой параметры
    /// 3. Препятствие автоматически блокирует/разблокирует клетки навмеша
    ///
    /// РЕЖИМЫ:
    /// - isDynamic = false: Для дверей/стен — обновляется только при вызове UpdateObstacle()
    /// - isDynamic = true: Для врагов — обновляется автоматически с заданным интервалом
    ///
    /// КАК ВЫРЕЗАЕТ:
    /// - Узлы ближе (радиус агента + carvingPadding) к форме блокируются — как при запекании,
    ///   иначе агенты задевали бы препятствие боком.
    /// - Узлы блокируются через журнал NavMesh3D (CarveNode/UncarveNode) со счётчиком,
    ///   поэтому два перекрывающихся препятствия не открывают узлы друг друга.
    /// - После перезапекания навмеша препятствие само вырезается заново.
    ///
    /// СВЯЗНОСТЬ:
    /// Регионы и граф HPA* строятся при запекании. Поиск пути видит вырезанные узлы
    /// (и при необходимости сам переходит на обычный A*), но если препятствие перекрывает
    /// проход ЦЕЛИКОМ (дверь, завал), включи rebuildConnectivity — иначе поиск будет
    /// долго искать путь, которого нет.
    ///
    /// ПРОИЗВОДИТЕЛЬНОСТЬ:
    /// - Обновление затрагивает только узлы в bounds препятствия
    /// - Для движущихся объектов используй updateInterval >= 0.3 сек
    /// </summary>
    public class NavMesh3DObstacle : MonoBehaviour
    {
        // ===== Настройки =====

        [Header("Obstacle Settings")]
        [Tooltip("Форма препятствия")]
        public ObstacleShape shape = ObstacleShape.Box;

        [Tooltip("Размер препятствия в метрах вдоль осей объекта (Box; для Capsule — высота по оси)")]
        public Vector3 size = Vector3.one;

        [Tooltip("Радиус препятствия (Sphere, Capsule)")]
        [Range(0.1f, 10f)]
        public float radius = 1f;

        [Tooltip("Ось капсулы: 0 — X, 1 — Y, 2 — Z")]
        [Range(0, 2)]
        public int capsuleAxis = 1;

        [Tooltip("Смещение центра формы относительно объекта, в метрах вдоль его осей")]
        public Vector3 center = Vector3.zero;

        [Tooltip("Использовать размеры из Collider автоматически")]
        public bool useColliderSize = true;

        [Header("Dynamic Settings")]
        [Tooltip("Динамическое препятствие (обновляется автоматически)")]
        public bool isDynamic = false;

        [Tooltip("Интервал обновления для динамических препятствий (сек)")]
        [Range(0.1f, 2f)]
        public float updateInterval = 0.5f;

        [Tooltip("Обновлять только при движении")]
        public bool updateOnlyWhenMoving = true;

        [Tooltip("Порог движения для обновления")]
        [Range(0.01f, 1f)]
        public float movementThreshold = 0.1f;

        [Tooltip("Очистить область при старте (для объектов которые были запечены как статические). " +
                 "При запекании из пещеры не нужно: такие коллайдеры не запекаются")]
        public bool clearOnStart = true;

        [Header("Carving")]
        [Tooltip("Активно ли препятствие (блокирует клетки)")]
        public bool carving = true;

        [Tooltip("Дополнительный отступ вокруг препятствия (сверх радиуса агента)")]
        [Range(0f, 2f)]
        public float carvingPadding = 0.1f;

        [Tooltip("Перестраивать связность (регионы и HPA*) при каждом изменении. " +
                 "Для дверей и завалов, которые перекрывают проход целиком. Дорого — не для движущихся")]
        public bool rebuildConnectivity = false;

        [Header("Debug")]
        public bool showBounds = true;

        // ===== Публичные свойства =====

        /// <summary>Границы препятствия в мировых координатах (с отступом)</summary>
        public Bounds WorldBounds => GetWorldBounds(Margin);

        /// <summary>Количество вырезанных узлов при последнем обновлении</summary>
        public int AffectedChunksCount { get; private set; }

        /// <summary>Время последнего обновления</summary>
        public float LastUpdateTime { get; private set; }

        // ===== Приватные поля =====

        private NavMesh3D navMesh;
        private Vector3 lastPosition;
        private Quaternion lastRotation;
        private float lastUpdateCheck;
        private bool wasCarving;
        private int appliedBakeVersion = -1;

        // Узлы, которые вырезало это препятствие
        private readonly List<int3> carvedNodes = new List<int3>();
        private readonly HashSet<long> carvedSet = new HashSet<long>();

        private Collider cachedCollider;

        private float Margin => carvingPadding + (navMesh != null ? navMesh.agentRadius : 0f);

        // ===== Unity Lifecycle =====

        private void Awake()
        {
            cachedCollider = GetComponent<Collider>();

            if (useColliderSize && cachedCollider != null)
            {
                UpdateSizeFromCollider();
            }
        }

        private void Start()
        {
            navMesh = NavMesh3D.Instance;

            if (navMesh == null)
            {
                Debug.LogWarning($"NavMesh3DObstacle on {gameObject.name}: NavMesh3D not found!");
                enabled = false;
                return;
            }

            lastPosition = transform.position;
            lastRotation = transform.rotation;
            wasCarving = false;
        }

        private void Update()
        {
            if (navMesh == null || !navMesh.IsBaked)
                return;

            // Первое запекание или навмеш перезапекли (например новая пещера) — старые узлы недействительны
            if (appliedBakeVersion != navMesh.BakeVersion)
            {
                appliedBakeVersion = navMesh.BakeVersion;
                carvedNodes.Clear();
                carvedSet.Clear();

                // Очищаем область от препятствия, которое запеклось вместе с навмешем (только физика)
                if (clearOnStart && !navMesh.BakedFromField)
                    ClearArea();

                wasCarving = false;
                UpdateObstacle();
                return;
            }

            if (!isDynamic)
                return;

            // Проверяем интервал
            if (Time.time - lastUpdateCheck < updateInterval)
                return;

            lastUpdateCheck = Time.time;

            // Проверяем нужно ли обновлять
            bool needsUpdate = false;

            if (carving != wasCarving)
            {
                needsUpdate = true;
            }
            else if (carving)
            {
                if (updateOnlyWhenMoving)
                {
                    float moved = Vector3.Distance(transform.position, lastPosition);
                    float rotated = Quaternion.Angle(transform.rotation, lastRotation);

                    needsUpdate = moved > movementThreshold || rotated > 5f;
                }
                else
                {
                    needsUpdate = true;
                }
            }

            if (needsUpdate)
            {
                UpdateObstacle();
            }
        }

        private void OnEnable()
        {
            if (navMesh != null && navMesh.IsBaked && appliedBakeVersion == navMesh.BakeVersion && carving)
            {
                UpdateObstacle();
            }
        }

        private void OnDisable()
        {
            if (navMesh != null && navMesh.IsBaked)
            {
                RemoveObstacle();
                wasCarving = false;
            }
        }

        // ===== Публичные методы =====

        /// <summary>
        /// Обновляет препятствие вручную.
        /// Вызывай для статических препятствий (двери) когда состояние изменилось.
        /// </summary>
        public void UpdateObstacle()
        {
            if (navMesh == null || !navMesh.IsBaked)
                return;

            bool changed = carvedNodes.Count > 0 || carving;

            // Сначала снимаем старое вырезание, потом накладываем новое
            RemoveObstacle();
            if (carving)
                ApplyObstacle();

            if (changed && rebuildConnectivity)
                navMesh.RebuildRegions();

            lastPosition = transform.position;
            lastRotation = transform.rotation;
            wasCarving = carving;
            LastUpdateTime = Time.time;
        }

        /// <summary>
        /// Включает/выключает препятствие.
        /// </summary>
        public void SetCarving(bool enabled)
        {
            if (carving == enabled)
                return;

            carving = enabled;
            UpdateObstacle();
        }

        // ===== Приватные методы =====

        private void ApplyObstacle()
        {
            float margin = Margin;
            ForEachNodeInBounds(GetWorldBounds(margin), p =>
            {
                Vector3 worldPos = navMesh.NodeToWorld(GlobalNodeCoord.FromAbsolute(p));
                if (DistanceToShape(worldPos) > margin)
                    return;

                long key = Key(p);
                if (!carvedSet.Add(key))
                    return;

                navMesh.CarveNode(p);
                carvedNodes.Add(p);
            });

            AffectedChunksCount = carvedNodes.Count;
        }

        private void RemoveObstacle()
        {
            foreach (var p in carvedNodes)
                navMesh.UncarveNode(p);

            carvedNodes.Clear();
            carvedSet.Clear();
            AffectedChunksCount = 0;
        }

        /// <summary>
        /// Очищает область препятствия — делает узлы внутри формы (с отступом) проходимыми.
        /// Для препятствий, которые физическое запекание приняло за статику.
        /// </summary>
        private void ClearArea()
        {
            float margin = Margin;
            ForEachNodeInBounds(GetWorldBounds(margin), p =>
            {
                Vector3 worldPos = navMesh.NodeToWorld(GlobalNodeCoord.FromAbsolute(p));
                if (DistanceToShape(worldPos) <= margin)
                    navMesh.ForceWalkable(p);
            });
        }

        private void ForEachNodeInBounds(Bounds b, System.Action<int3> action)
        {
            // Диапазон узлов по 8 углам bounds (навмеш может быть повёрнут)
            int3 limit = navMesh.ChunkCount * NavMesh3DSettings.ChunkSize;
            int3 min = limit, max = -1;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    (i & 1) == 0 ? b.min.x : b.max.x,
                    (i & 2) == 0 ? b.min.y : b.max.y,
                    (i & 4) == 0 ? b.min.z : b.max.z);
                int3 n = navMesh.WorldToNode(corner).ToAbsolute();
                min = math.min(min, n);
                max = math.max(max, n);
            }
            min = math.max(min, 0);
            max = math.min(max, limit - 1);

            for (int x = min.x; x <= max.x; x++)
            for (int y = min.y; y <= max.y; y++)
            for (int z = min.z; z <= max.z; z++)
                action(new int3(x, y, z));
        }

        private Vector3 ShapeCenter => transform.TransformPoint(Vector3.zero) + transform.rotation * center;

        /// <summary>Расстояние от точки до формы (0 внутри), в метрах.</summary>
        private float DistanceToShape(Vector3 worldPoint)
        {
            Vector3 p = Quaternion.Inverse(transform.rotation) * (worldPoint - ShapeCenter);

            switch (shape)
            {
                case ObstacleShape.Box:
                    Vector3 half = size * 0.5f;
                    Vector3 q = new Vector3(
                        Mathf.Max(Mathf.Abs(p.x) - half.x, 0f),
                        Mathf.Max(Mathf.Abs(p.y) - half.y, 0f),
                        Mathf.Max(Mathf.Abs(p.z) - half.z, 0f));
                    return q.magnitude;

                case ObstacleShape.Sphere:
                    return Mathf.Max(p.magnitude - radius, 0f);

                case ObstacleShape.Capsule:
                    float halfSegment = Mathf.Max(0f, size[capsuleAxis] * 0.5f - radius);
                    float along = Mathf.Clamp(p[capsuleAxis], -halfSegment, halfSegment);
                    Vector3 onAxis = Vector3.zero;
                    onAxis[capsuleAxis] = along;
                    return Mathf.Max((p - onAxis).magnitude - radius, 0f);

                default:
                    return float.MaxValue;
            }
        }

        private Bounds GetWorldBounds(float margin)
        {
            Vector3 localExtents;
            switch (shape)
            {
                case ObstacleShape.Box:
                    localExtents = size * 0.5f;
                    break;
                case ObstacleShape.Sphere:
                    return new Bounds(ShapeCenter, Vector3.one * (radius + margin) * 2f);
                case ObstacleShape.Capsule:
                    localExtents = Vector3.one * radius;
                    localExtents[capsuleAxis] = Mathf.Max(size[capsuleAxis] * 0.5f, radius);
                    break;
                default:
                    localExtents = Vector3.one;
                    break;
            }

            // AABB повёрнутого бокса: |R| * extents
            Matrix4x4 r = Matrix4x4.Rotate(transform.rotation);
            Vector3 world = new Vector3(
                Mathf.Abs(r.m00) * localExtents.x + Mathf.Abs(r.m01) * localExtents.y + Mathf.Abs(r.m02) * localExtents.z,
                Mathf.Abs(r.m10) * localExtents.x + Mathf.Abs(r.m11) * localExtents.y + Mathf.Abs(r.m12) * localExtents.z,
                Mathf.Abs(r.m20) * localExtents.x + Mathf.Abs(r.m21) * localExtents.y + Mathf.Abs(r.m22) * localExtents.z);

            return new Bounds(ShapeCenter, (world + Vector3.one * margin) * 2f);
        }

        private static long Key(int3 p) => ((long)p.x & 0xFFFFF) | (((long)p.y & 0xFFFFF) << 20) | (((long)p.z & 0xFFFFF) << 40);

        /// <summary>
        /// Размеры из коллайдера в метрах вдоль осей объекта (с учётом масштаба, но без поворота —
        /// поворот учитывается при проверке).
        /// </summary>
        private void UpdateSizeFromCollider()
        {
            Vector3 scale = transform.lossyScale;
            scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));

            if (cachedCollider is BoxCollider box)
            {
                shape = ObstacleShape.Box;
                size = Vector3.Scale(box.size, scale);
                center = Vector3.Scale(box.center, scale);
            }
            else if (cachedCollider is SphereCollider sphere)
            {
                shape = ObstacleShape.Sphere;
                radius = sphere.radius * Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z));
                center = Vector3.Scale(sphere.center, scale);
            }
            else if (cachedCollider is CapsuleCollider capsule)
            {
                shape = ObstacleShape.Capsule;
                capsuleAxis = capsule.direction;
                float sideScale = 0f;
                for (int i = 0; i < 3; i++)
                    if (i != capsuleAxis) sideScale = Mathf.Max(sideScale, scale[i]);
                radius = capsule.radius * sideScale;
                size = Vector3.one * radius * 2f;
                size[capsuleAxis] = Mathf.Max(capsule.height * scale[capsuleAxis], radius * 2f);
                center = Vector3.Scale(capsule.center, scale);
            }
        }

        // ===== Gizmos =====

        private void OnDrawGizmosSelected()
        {
            if (!showBounds)
                return;

            Gizmos.color = carving ? new Color(1, 0, 0, 0.3f) : new Color(0, 1, 0, 0.3f);

            Matrix4x4 oldMatrix = Gizmos.matrix;
            // Размеры уже в метрах — масштаб объекта не применяем
            Gizmos.matrix = Matrix4x4.TRS(ShapeCenter, transform.rotation, Vector3.one);

            switch (shape)
            {
                case ObstacleShape.Box:
                    Gizmos.DrawCube(Vector3.zero, size);
                    Gizmos.DrawWireCube(Vector3.zero, size);
                    break;

                case ObstacleShape.Sphere:
                    Gizmos.DrawSphere(Vector3.zero, radius);
                    Gizmos.DrawWireSphere(Vector3.zero, radius);
                    break;

                case ObstacleShape.Capsule:
                    DrawCapsuleGizmo();
                    break;
            }

            Gizmos.matrix = oldMatrix;

            // Область вырезания с учётом радиуса агента
            Gizmos.color = new Color(1, 1, 0, 0.2f);
            var bounds = GetWorldBounds(Margin);
            Gizmos.DrawWireCube(bounds.center, bounds.size);
        }

        private void DrawCapsuleGizmo()
        {
            float halfHeight = Mathf.Max(0, size[capsuleAxis] * 0.5f - radius);
            Vector3 axis = Vector3.zero;
            axis[capsuleAxis] = 1f;
            Vector3 side1 = Vector3.zero, side2 = Vector3.zero;
            side1[(capsuleAxis + 1) % 3] = radius;
            side2[(capsuleAxis + 2) % 3] = radius;

            Vector3 up = axis * halfHeight;
            Vector3 down = -axis * halfHeight;

            Gizmos.DrawWireSphere(up, radius);
            Gizmos.DrawWireSphere(down, radius);

            Gizmos.DrawLine(up + side1, down + side1);
            Gizmos.DrawLine(up - side1, down - side1);
            Gizmos.DrawLine(up + side2, down + side2);
            Gizmos.DrawLine(up - side2, down - side2);
        }
    }

    /// <summary>
    /// Форма препятствия.
    /// </summary>
    public enum ObstacleShape
    {
        Box,
        Sphere,
        Capsule
    }
}
