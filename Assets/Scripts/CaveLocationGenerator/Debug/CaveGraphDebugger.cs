using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Статический дебаг графа пещеры. Развязывает "подачу данных" и "отрисовку":
///   - Show(nodes)  — толкаешь список откуда угодно (например из GraphBuilder.BuildGraph)
///   - DrawCached() — хост рисует кэш из OnDrawGizmos
///   - Draw(nodes)  — прямая отрисовка, если ты уже в gizmo-контексте
///
/// Пространство повторяет MarchingCubesDebug: тот же matchBackendCentering /
/// applyVoxelSize. Держи их одинаковыми, иначе граф разъедется с мешем.
/// </summary>
public static class CaveGraphDebug
{
    // ------------------------------------------------- кэш (подача из любого места)
    private static List<GraphNode> _nodes;

    public static void Show(IList<GraphNode> nodes)
        => _nodes = nodes != null ? new List<GraphNode>(nodes) : null;

    public static void Clear() => _nodes = null;

    public static int CachedCount => _nodes?.Count ?? 0;

    [System.Serializable]
    public class Options
    {
        [Header("Пространство (как в MarchingCubesDebug)")]
        public bool matchBackendCentering = true;
        public bool applyVoxelSize = false;

        [Header("Ноды")]
        public bool drawNodes = true;
        [Min(0f)] public float nodeRadius = 0.6f;
        [Tooltip("Вертикальная риска у каждой ноды — они лежат на y=0, так заметнее")]
        public bool drawGroundTick = true;
        [Min(0f)] public float tickHeight = 2f;

        [Header("Якоря")]
        [Tooltip("Кольцо вокруг IsAnchor-нод")]
        public bool highlightAnchors = true;
        public Color anchorRingColor = Color.white;
        [Min(1f)] public float anchorRingScale = 1.8f;

        [Header("Подписи (только в редакторе)")]
        public bool drawLabels = true;
        public bool labelIndex = true;
        public bool labelRole = true;
        [Min(0f)] public float labelMaxDistance = 40f;

        [Header("Цвета ролей")]
        public Color entrance = new Color(0.3f, 1f, 0.3f);
        public Color objective = new Color(1f, 0.85f, 0.1f);
        public Color sideMission = new Color(0.3f, 0.8f, 1f);
        public Color loot = new Color(1f, 0.5f, 0.1f);
        public Color exit = new Color(1f, 0.25f, 0.25f);
        public Color empty = new Color(0.6f, 0.6f, 0.6f);
    }

    public static Color ColorFor(Options o, NodeRole role)
    {
        switch (role)
        {
            case NodeRole.Entrance:    return o.entrance;
            case NodeRole.Objective:   return o.objective;
            case NodeRole.SideMission: return o.sideMission;
            case NodeRole.Loot:        return o.loot;
            case NodeRole.Exit:        return o.exit;
            default:                   return o.empty;
        }
    }

    // ------------------------------------------------- отрисовка
    public static void DrawCached(MarchingCubesConfig config, Transform space, Options o)
        => Draw(_nodes, config, space, o);

    public static void Draw(IList<GraphNode> nodes, MarchingCubesConfig config, Transform space, Options o)
    {
        if (nodes == null || nodes.Count == 0 || o == null || !o.drawNodes) return;

        Matrix4x4 m = BuildSpaceMatrix(config, space, o.matchBackendCentering, o.applyVoxelSize);
        Matrix4x4 prev = Gizmos.matrix;
        Gizmos.matrix = m;

        Camera cam = Camera.current;

        for (int i = 0; i < nodes.Count; i++)
        {
            GraphNode n = nodes[i];
            Vector3 p = n.Position; // сырые координаты ноды; матрица применит centering/scale/трансформ

            Gizmos.color = ColorFor(o, n.Role);
            Gizmos.DrawSphere(p, o.nodeRadius);

            if (o.drawGroundTick)
                Gizmos.DrawLine(p, p + Vector3.up * o.tickHeight);

            if (o.highlightAnchors && n.IsAnchor)
            {
                Gizmos.color = o.anchorRingColor;
                Gizmos.DrawWireSphere(p, o.nodeRadius * o.anchorRingScale);
            }

#if UNITY_EDITOR
            if (o.drawLabels && (o.labelIndex || o.labelRole))
            {
                Vector3 wp = m.MultiplyPoint3x4(p + Vector3.up * (o.tickHeight + 0.5f));
                if (cam == null || Vector3.Distance(cam.transform.position, wp) <= o.labelMaxDistance)
                {
                    string text = (o.labelIndex ? $"#{i}" : string.Empty)
                                + (o.labelIndex && o.labelRole ? " " : string.Empty)
                                + (o.labelRole ? n.Role.ToString() : string.Empty);
                    Handles.color = ColorFor(o, n.Role);
                    Handles.Label(wp, text);
                }
            }
#endif
        }

        Gizmos.matrix = prev;
    }

    /// <summary>
    /// Та же формула отображения, что в MarchingCubesDebug.
    /// Если будешь рисовать рёбра/петли — гони их через эту же матрицу.
    /// </summary>
    public static Matrix4x4 BuildSpaceMatrix(
        MarchingCubesConfig config, Transform space, bool matchCentering, bool applyVoxelSize)
    {
        Vector3 offset = Vector3.zero;
        float scale = 1f;

        if (config != null)
        {
            if (matchCentering)
                offset = -Vector3.Scale(config.WorldSize, config.ChunkSize) * 0.5f;
            if (applyVoxelSize)
                scale = config.VoxelSize;
        }

        Matrix4x4 voxelToDisplay =
            Matrix4x4.TRS(offset * scale, Quaternion.identity, Vector3.one * scale);

        return (space != null ? space.localToWorldMatrix : Matrix4x4.identity) * voxelToDisplay;
    }
}

/// <summary>
/// Тонкий хост: рисует закэшированные через CaveGraphDebug.Show(...) ноды.
/// Вешается на тот же объект, что и CaveGenerator/MarchingCubesDebugger.
/// </summary>
public class CaveGraphDebugger : MonoBehaviour
{
    [SerializeField] private MarchingCubesConfig config;

    [Tooltip("Рисовать только когда объект выделен")]
    [SerializeField] private bool onlyWhenSelected = false;

    [SerializeField] private CaveGraphDebug.Options options = new CaveGraphDebug.Options();

    private void OnDrawGizmos()
    {
        if (!onlyWhenSelected) CaveGraphDebug.DrawCached(config, transform, options);
    }

    private void OnDrawGizmosSelected()
    {
        if (onlyWhenSelected) CaveGraphDebug.DrawCached(config, transform, options);
    }
}