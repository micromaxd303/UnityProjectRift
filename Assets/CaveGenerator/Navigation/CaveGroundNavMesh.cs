using System;
using System.Collections;
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Этап 6 для наземных врагов: после генерации пещеры запекает NavMesh для каждого
/// NavMeshSurface на этом объекте (один surface — один тип агента: Острык, Молотильник...)
/// и добавляет ссылки на обрывах, чтобы враги спрыгивали вниз.
///
/// Лазы отдельных ссылок не требуют: узкий туннель сам не попадёт в NavMesh крупного агента,
/// если он не пролезает по радиусу или росту.
///
/// Запекание асинхронное: пока не придёт NavMeshReady, наземных врагов не ставить.
/// </summary>
[RequireComponent(typeof(CaveGenerator))]
public class CaveGroundNavMesh : MonoBehaviour
{
    [Serializable]
    public class AgentLinks
    {
        public NavMeshSurface surface;

        [Tooltip("С какой высоты агент спрыгивает, м. 0 — без ссылок-обрывов.")]
        [Min(0f)] public float maxDrop = 10f;

        [Tooltip("На какую высоту агент запрыгивает обратно, м. 0 — только вниз.")]
        [Min(0f)] public float maxJumpUp;
    }

    [Tooltip("Типы агентов. Пусто — все NavMeshSurface на объекте с настройками по умолчанию.")]
    [SerializeField] private List<AgentLinks> agents = new();

    [Header("Ссылки на обрывах")]
    [Tooltip("Не ставить ссылки ближе друг к другу, м.")]
    [Min(0.5f)] [SerializeField] private float linkSpacing = 3f;

    [Tooltip("Не ставить ссылку, если по NavMesh уже есть путь вниз не длиннее перепада × это число.")]
    [Min(1f)] [SerializeField] private float detourRatio = 4f;

    [Tooltip("Насколько далеко от края искать точку приземления по горизонтали, м.")]
    [Min(0f)] [SerializeField] private float landingReach = 1.5f;

    [Tooltip("После запекания вернуть на NavMesh агентов, которые с него слетели, если новый NavMesh не дальше этого, м. 0 — не трогать.")]
    [Min(0f)] [SerializeField] private float reattachRadius = 5f;

    [Header("Debug")]
    [SerializeField] private bool logStats = true;
    [SerializeField] private bool showLinks = true;

    private CaveGenerator _generator;
    private Coroutine _bake;
    private int _bakeId;
    private readonly List<NavMeshData> _ownData = new();
    private readonly List<NavMeshLinkInstance> _links = new();
    private readonly List<(Vector3 a, Vector3 b, int agent, bool both)> _linkGizmos = new();

    /// <summary>NavMesh и ссылки готовы, можно ставить наземных врагов.</summary>
    public bool IsReady { get; private set; }

    public event Action NavMeshReady;

    private void Reset()
    {
        foreach (var s in GetComponents<NavMeshSurface>())
            agents.Add(new AgentLinks { surface = s });
    }

    private void Awake() => _generator = GetComponent<CaveGenerator>();

    private void OnEnable()
    {
        _generator.KeepMeshesReadable = true;
        _generator.LevelGenerated += OnLevelGenerated;
    }

    private void OnDisable()
    {
        if (_generator == null) return;
        _generator.LevelGenerated -= OnLevelGenerated;
        _generator.KeepMeshesReadable = false;
        _generator.ReleaseMeshData();
        Clear();
    }

    private void OnLevelGenerated(LevelContext context) => Bake();

    private void Start()
    {
        // Пещера могла быть построена раньше, чем этот компонент подписался
        if (_generator.Context != null && !IsReady && _bake == null)
            Bake();
    }

    [ContextMenu("Bake Ground NavMesh")]
    public void Bake()
    {
        if (_bake != null)
            StopCoroutine(_bake);
        _bake = StartCoroutine(BakeRoutine(++_bakeId));
    }

    private IEnumerator BakeRoutine(int id)
    {
        IsReady = false;
        Clear();

        // Старые чанки уничтожаются в конце кадра генерации: собираем геометрию со следующего
        yield return null;
        if (id != _bakeId)
            yield break;

        var list = ResolveAgents();
        if (list.Count == 0)
        {
            Debug.LogWarning("CaveGroundNavMesh: нет ни одного NavMeshSurface на объекте пещеры.");
            _generator.ReleaseMeshData();
            yield break;
        }

        var timer = System.Diagnostics.Stopwatch.StartNew();
        var ops = new List<AsyncOperation>();

        foreach (var a in list)
        {
            var s = a.surface;
            // Собираем только детей пещеры: чанки меша, без врагов и пропсов снаружи
            s.collectObjects = CollectObjects.Children;
            s.useGeometry = NavMeshCollectGeometry.RenderMeshes;

            s.RemoveData();
            var data = new NavMeshData(s.agentTypeID)
            {
                name = $"Cave NavMesh ({NavMesh.GetSettingsNameFromID(s.agentTypeID)})",
                position = s.transform.position,
                rotation = s.transform.rotation
            };
            _ownData.Add(data);
            s.navMeshData = data;
            s.AddData();
            ops.Add(s.UpdateNavMesh(data));
        }

        if (logStats)
            Debug.Log($"Наземный NavMesh: запекаю {list.Count} агент(ов)...");

        float nextReport = Time.realtimeSinceStartup + 5f;
        foreach (var op in ops)
        {
            if (op == null)
            {
                Debug.LogError("CaveGroundNavMesh: UpdateNavMesh не запустился (нет геометрии?)");
                continue;
            }
            while (!op.isDone)
            {
                if (logStats && Time.realtimeSinceStartup >= nextReport)
                {
                    nextReport += 5f;
                    float p = 0f;
                    foreach (var o in ops) p += o != null ? o.progress : 1f;
                    Debug.Log($"Наземный NavMesh: {p / ops.Count * 100f:F0}% за {timer.ElapsedMilliseconds / 1000f:F0} с");
                }
                yield return null;
            }
        }

        if (id != _bakeId)
            yield break;

        long bakeMs = timer.ElapsedMilliseconds;
        timer.Restart();

        // Обрывы ищем по полю вокселей, проверяем по NavMesh каждого агента
        var drops = FindDropCandidates();
        var stats = new List<string>();
        for (int i = 0; i < list.Count; i++)
            stats.Add(AddDropLinks(list[i], i, drops));

        // Меши больше никто не читает
        _generator.ReleaseMeshData();

        if (logStats)
            Debug.Log($"Наземный NavMesh: запекание {bakeMs} мс, ссылки {timer.ElapsedMilliseconds} мс, " +
                      $"кандидатов обрывов {drops.Count}\n" + string.Join("\n", stats));

        int reattached = ReattachAgents();
        if (logStats && reattached > 0)
            Debug.Log($"Наземный NavMesh: возвращено на NavMesh агентов: {reattached}");

        _bake = null;
        IsReady = true;
        NavMeshReady?.Invoke();
    }

    /// <summary>
    /// Агенты, стоявшие на старом NavMesh, при его удалении «отваливаются» от него
    /// (предупреждение «Failed to create agent...»). Ставим их на ближайшую точку нового.
    /// </summary>
    private int ReattachAgents()
    {
        if (reattachRadius <= 0f)
            return 0;

        int count = 0;
        foreach (var agent in FindObjectsByType<NavMeshAgent>(FindObjectsSortMode.None))
        {
            if (!agent.isActiveAndEnabled || agent.isOnNavMesh)
                continue;
            var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = NavMesh.AllAreas };
            if (NavMesh.SamplePosition(agent.transform.position, out var hit, reattachRadius, filter) && agent.Warp(hit.position))
                count++;
        }
        return count;
    }

    private List<AgentLinks> ResolveAgents()
    {
        var result = new List<AgentLinks>();
        foreach (var a in agents)
            if (a.surface != null)
                result.Add(a);

        if (result.Count == 0)
            foreach (var s in GetComponents<NavMeshSurface>())
                result.Add(new AgentLinks { surface = s });

        return result;
    }

    // ===== Обрывы =====

    private struct Drop
    {
        public Vector3 Top;     // край, мир
        public Vector3 Bottom;  // под краем, мир
        public float Height;
    }

    /// <summary>
    /// Пол, у которого в соседней колонке воздух уходит вниз: край обрыва.
    /// Кандидат = точка на краю и пол под ним. Прорежены сеткой linkSpacing.
    /// </summary>
    private List<Drop> FindDropCandidates()
    {
        var result = new List<Drop>();
        var v = _generator.Validation;
        var f = v?.Field;
        if (f == null)
        {
            Debug.LogWarning("CaveGroundNavMesh: нет поля вокселей (включи проверку уровня, этап 4), ссылки на обрывах не строятся.");
            return result;
        }

        float maxDrop = 0f;
        foreach (var a in ResolveAgents())
            maxDrop = Mathf.Max(maxDrop, a.maxDrop);
        if (maxDrop <= 0f)
            return result;

        float vs = f.VoxelSize;
        int maxV = Mathf.FloorToInt(maxDrop / vs);
        int minV = Mathf.Max(2, Mathf.CeilToInt(0.75f / vs));

        // Поверхности из анализа: Cleanup только заливает карманы, поэтому IsAir ниже это отсеет
        var surface = v.Analysis?.Surface;
        if (surface == null || surface.Length != f.Count)
            surface = LevelAnalyzer.ClassifySurfaces(f, Mathf.Cos(55f * Mathf.Deg2Rad));

        var taken = new HashSet<Vector3Int>();
        var root = _generator.transform;
        int[] dx = { 1, -1, 0, 0, 1, 1, -1, -1 };
        int[] dz = { 0, 0, 1, -1, 1, -1, 1, -1 };

        for (int z = 1; z < f.Size.z - 1; z++)
        for (int y = 2; y < f.Size.y - 2; y++)
        for (int x = 1; x < f.Size.x - 1; x++)
        {
            int i = f.Index(x, y, z);
            if (surface[i] != (byte)SurfaceClass.Floor || !f.IsAir(i) || f.IsAir(x, y - 1, z) || !f.IsAir(x, y + 1, z))
                continue;

            for (int d = 0; d < 8; d++)
            {
                int nx = x + dx[d], nz = z + dz[d];
                // Соседняя колонка: на уровне пола и чуть ниже воздух, иначе это стена или ступенька
                if (!f.IsAir(nx, y, nz) || !f.IsAir(nx, y + 1, nz) || !f.IsAir(nx, y - 1, nz))
                    continue;

                int land = -1;
                for (int yy = y - 2; yy >= Mathf.Max(1, y - maxV); yy--)
                {
                    if (!f.IsAir(nx, yy, nz))
                    {
                        land = yy + 1;
                        break;
                    }
                }
                if (land < 0 || y - land < minV)
                    continue;

                var top = f.ToLocal(x, y, z);
                var cell = Vector3Int.FloorToInt(top / linkSpacing);
                if (!taken.Add(cell))
                    break;

                var dir = new Vector3(dx[d], 0f, dz[d]).normalized;
                result.Add(new Drop
                {
                    Top = root.TransformPoint(top),
                    Bottom = root.TransformPoint(f.ToLocal(nx, land, nz) + dir * landingReach),
                    Height = (y - land) * vs
                });
                break;
            }
        }

        return result;
    }

    private string AddDropLinks(AgentLinks a, int agentIndex, List<Drop> drops)
    {
        int type = a.surface.agentTypeID;
        string name = NavMesh.GetSettingsNameFromID(type);
        if (a.maxDrop <= 0f)
            return $"{name}: ссылки выключены";

        var settings = NavMesh.GetSettingsByID(type);
        float radius = settings.agentRadius;
        float minDrop = Mathf.Max(settings.agentClimb + 0.25f, 0.75f);
        float search = radius + 1f;

        var filter = new NavMeshQueryFilter { agentTypeID = type, areaMask = NavMesh.AllAreas };
        var path = new NavMeshPath();
        var taken = new HashSet<Vector3Int>();
        int added = 0, skippedDetour = 0;

        foreach (var drop in drops)
        {
            if (drop.Height > a.maxDrop + 0.5f)
                continue;
            if (!NavMesh.SamplePosition(drop.Top, out var top, search, filter))
                continue;
            if (!NavMesh.SamplePosition(drop.Bottom, out var bottom, search, filter))
                continue;

            // Низ не должен «прилипнуть» обратно к верхнему краю
            float h = top.position.y - bottom.position.y;
            if (h < minDrop || h > a.maxDrop + 0.5f)
                continue;
            var flat = bottom.position - top.position;
            flat.y = 0f;
            if (flat.magnitude > radius * 2f + landingReach + 1.5f)
                continue;

            // Несколько кандидатов сошлись в одну точку NavMesh
            if (!taken.Add(Vector3Int.FloorToInt(top.position / linkSpacing)))
                continue;

            // Рядом уже есть пандус или ступени
            if (NavMesh.CalculatePath(top.position, bottom.position, filter, path)
                && path.status == NavMeshPathStatus.PathComplete
                && PathLength(path) <= h * detourRatio)
            {
                skippedDetour++;
                continue;
            }

            bool both = a.maxJumpUp > 0f && h <= a.maxJumpUp;
            var link = NavMesh.AddLink(new NavMeshLinkData
            {
                startPosition = top.position,
                endPosition = bottom.position,
                width = 0f,
                costModifier = -1f,
                bidirectional = both,
                area = 0,
                agentTypeID = type
            });
            if (!NavMesh.IsLinkValid(link))
                continue;

            NavMesh.SetLinkOwner(link, this);
            _links.Add(link);
            _linkGizmos.Add((top.position, bottom.position, agentIndex, both));
            added++;
        }

        return $"{name}: ссылок {added} (пропущено из-за обхода рядом: {skippedDetour})";
    }

    private static float PathLength(NavMeshPath path)
    {
        var c = path.corners;
        float len = 0f;
        for (int i = 1; i < c.Length; i++)
            len += Vector3.Distance(c[i - 1], c[i]);
        return len;
    }

    private void Clear()
    {
        foreach (var l in _links)
            NavMesh.RemoveLink(l);
        _links.Clear();
        _linkGizmos.Clear();

        foreach (var a in ResolveAgents())
            if (_ownData.Contains(a.surface.navMeshData))
            {
                a.surface.RemoveData();
                a.surface.navMeshData = null;
            }
        foreach (var d in _ownData)
            if (d != null)
                Destroy(d);
        _ownData.Clear();
        IsReady = false;
    }

    private static readonly Color[] LinkColors =
    {
        new Color(1f, 0.6f, 0.1f), new Color(0.3f, 0.9f, 1f), new Color(0.9f, 0.3f, 1f), new Color(0.4f, 1f, 0.4f)
    };

    private void OnDrawGizmosSelected()
    {
        if (!showLinks) return;
        foreach (var (a, b, agent, both) in _linkGizmos)
        {
            Gizmos.color = LinkColors[agent % LinkColors.Length];
            // Дуга: подъём над краем и спуск
            Vector3 prev = a;
            for (int k = 1; k <= 8; k++)
            {
                float t = k / 8f;
                var p = Vector3.Lerp(a, b, t) + Vector3.up * (4f * t * (1f - t) * 0.6f);
                Gizmos.DrawLine(prev, p);
                prev = p;
            }
            Gizmos.DrawSphere(a, 0.12f);
            Gizmos.DrawWireSphere(b, both ? 0.2f : 0.12f);
        }
    }
}
