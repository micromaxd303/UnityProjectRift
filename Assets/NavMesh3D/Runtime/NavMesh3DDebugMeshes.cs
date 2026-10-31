using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace NavMesh3DSystem
{
    /// <summary>Что рисовать в гизмо навмеша.</summary>
    public enum NavMesh3DDebugMode
    {
        /// <summary>Ничего, кроме рамки объёма</summary>
        None,

        /// <summary>
        /// Срез: один слой узлов плоскими плитками. Видно форму проходимого пространства
        /// в сечении, как на томограмме.
        /// </summary>
        Slice,

        /// <summary>
        /// Оболочка: грани между проходимыми и заблокированными узлами. Показывает объём,
        /// где может летать агент, — пещеру, «сжатую» на радиус агента. Цвет — регион.
        /// </summary>
        Hull,

        /// <summary>Узлы рядом с камерой, только пограничные (у которых есть сосед другого типа)</summary>
        Nodes
    }

    public enum NavMesh3DSliceAxis { X, Y, Z }

    /// <summary>
    /// Строит меши для гизмо. Меш кэшируется и пересобирается только при смене настроек
    /// или после запекания — тысячи DrawSphere каждый кадр больше не нужны.
    /// Вершины — в локальных координатах сетки (от её угла, в метрах).
    /// </summary>
    internal static class NavMesh3DDebugMeshes
    {
        public struct Batch
        {
            public Mesh mesh;
            public Color color;
            public string label;
            public int nodeCount;
        }

        /// <summary>Контрастные цвета для самых крупных регионов.</summary>
        private static readonly Color[] Palette =
        {
            new Color(0.25f, 0.75f, 1.0f), // голубой
            new Color(1.0f, 0.55f, 0.2f),  // оранжевый
            new Color(0.3f, 1.0f, 0.45f),  // зелёный
            new Color(1.0f, 0.3f, 0.8f),   // розовый
            new Color(1.0f, 0.95f, 0.3f),  // жёлтый
            new Color(0.65f, 0.4f, 1.0f),  // фиолетовый
            new Color(0.3f, 1.0f, 1.0f),   // циан
            new Color(0.9f, 0.25f, 0.25f), // красный
        };

        private static readonly Color OtherColor = new Color(0.6f, 0.6f, 0.6f);
        private static readonly Color BlockedColor = new Color(0.35f, 0.08f, 0.08f);
        private const int BlockedGroup = int.MinValue;
        private const int OtherGroup = int.MinValue + 1;

        private static readonly int3[] Axes =
        {
            new int3(1, 0, 0), new int3(-1, 0, 0),
            new int3(0, 1, 0), new int3(0, -1, 0),
            new int3(0, 0, 1), new int3(0, 0, -1)
        };

        private class Group
        {
            public readonly List<Vector3> vertices = new List<Vector3>();
            public readonly List<Vector3> normals = new List<Vector3>();
            public readonly List<int> triangles = new List<int>();
            public int nodes;
        }

        /// <summary>Срез сетки: слой layer по оси axis.</summary>
        public static List<Batch> BuildSlice(NavMesh3D navMesh, NavMesh3DSliceAxis axis, int layer,
            bool byRegion, bool showBlocked)
        {
            var groups = new Dictionary<int, Group>();
            int3 limit = navMesh.ChunkCount * NavMesh3DSettings.ChunkSize;
            float cell = navMesh.cellSize;
            int a = (int)axis;
            int u = (a + 1) % 3, v = (a + 2) % 3;

            float3 normal = float3.zero;
            normal[a] = 1f;

            int3 p = int3.zero;
            p[a] = math.clamp(layer, 0, limit[a] - 1);

            for (int i = 0; i < limit[u]; i++)
            for (int j = 0; j < limit[v]; j++)
            {
                p[u] = i;
                p[v] = j;

                bool walkable = navMesh.IsWalkableAbsolute(p);
                if (!walkable && !showBlocked)
                    continue;

                int key = walkable ? (byRegion ? navMesh.GetRegionAbsolute(p) : 0) : BlockedGroup;
                var g = GetGroup(groups, key);
                g.nodes++;

                // Плитка чуть меньше ячейки — между ними видна сетка
                float3 center = ((float3)p + 0.5f) * cell;
                AddQuad(g, center, normal, cell * 0.46f, true);
            }

            return Finish(groups, byRegion);
        }

        /// <summary>Оболочка проходимого объёма.</summary>
        public static List<Batch> BuildHull(NavMesh3D navMesh, bool byRegion)
        {
            var groups = new Dictionary<int, Group>();
            int3 cc = navMesh.ChunkCount;
            int size = NavMesh3DSettings.ChunkSize;
            int3 limit = cc * size;
            float cell = navMesh.cellSize;
            var chunks = navMesh.FlatChunks;

            for (int c = 0; c < chunks.Length; c++)
            {
                var chunk = chunks[c];
                if (chunk.IsFullyBlocked)
                    continue;

                int3 origin = new int3(chunk.Coord.x, chunk.Coord.y, chunk.Coord.z) * size;

                for (int n = 0; n < NavMesh3DChunk.NodeCount; n++)
                {
                    if (!chunk.IsWalkable(n))
                        continue;

                    int3 p = origin + new int3((n >> 4) & 15, n >> 8, n & 15);
                    Group g = null;

                    for (int d = 0; d < 6; d++)
                    {
                        int3 q = p + Axes[d];
                        bool open = math.all(q >= 0) && math.all(q < limit) && navMesh.IsWalkableAbsolute(q);
                        if (open)
                            continue;

                        if (g == null)
                        {
                            g = GetGroup(groups, byRegion ? navMesh.GetRegionAbsolute(p) : 0);
                            g.nodes++;
                        }

                        // Грань на границе ячейки, смотрит внутрь проходимого объёма:
                        // изнутри пещеры видна лицевая сторона
                        float3 dir = Axes[d];
                        float3 center = ((float3)p + 0.5f + dir * 0.5f) * cell;
                        AddQuad(g, center, -dir, cell * 0.5f, false);
                    }
                }
            }

            return Finish(groups, byRegion);
        }

        public static void Destroy(List<Batch> batches)
        {
            if (batches == null) return;
            foreach (var b in batches)
            {
                if (b.mesh == null) continue;
                if (Application.isPlaying) Object.Destroy(b.mesh);
                else Object.DestroyImmediate(b.mesh);
            }
            batches.Clear();
        }

        // ===== Внутреннее =====

        private static Group GetGroup(Dictionary<int, Group> groups, int key)
        {
            if (!groups.TryGetValue(key, out var g))
                groups[key] = g = new Group();
            return g;
        }

        private static void AddQuad(Group g, float3 center, float3 normal, float half, bool doubleSided)
        {
            // Две касательные, перпендикулярные нормали
            float3 t1 = math.abs(normal.x) > 0.5f ? new float3(0, 1, 0) : new float3(1, 0, 0);
            float3 t2 = math.cross(normal, t1);
            t1 *= half;
            t2 *= half;

            int start = g.vertices.Count;
            g.vertices.Add(center - t1 - t2);
            g.vertices.Add(center - t1 + t2);
            g.vertices.Add(center + t1 + t2);
            g.vertices.Add(center + t1 - t2);
            for (int i = 0; i < 4; i++) g.normals.Add(normal);

            // Порядок обхода под нормаль (в Unity лицевая сторона — по часовой стрелке)
            Vector3 tri = Vector3.Cross(g.vertices[start + 1] - g.vertices[start], g.vertices[start + 2] - g.vertices[start]);
            bool flip = Vector3.Dot(tri, normal) < 0f;
            AddTriangles(g.triangles, start, flip);

            if (doubleSided)
            {
                int back = g.vertices.Count;
                for (int i = 0; i < 4; i++)
                {
                    g.vertices.Add(g.vertices[start + i]);
                    g.normals.Add(-normal);
                }
                AddTriangles(g.triangles, back, !flip);
            }
        }

        private static void AddTriangles(List<int> tris, int s, bool flip)
        {
            if (flip)
            {
                tris.Add(s); tris.Add(s + 2); tris.Add(s + 1);
                tris.Add(s); tris.Add(s + 3); tris.Add(s + 2);
            }
            else
            {
                tris.Add(s); tris.Add(s + 1); tris.Add(s + 2);
                tris.Add(s); tris.Add(s + 2); tris.Add(s + 3);
            }
        }

        /// <summary>
        /// Крупнейшие регионы получают цвета палитры, остальные сливаются в серый «прочее».
        /// </summary>
        private static List<Batch> Finish(Dictionary<int, Group> groups, bool byRegion)
        {
            var keys = new List<int>(groups.Keys);
            keys.Sort((x, y) => groups[y].nodes.CompareTo(groups[x].nodes));

            var colored = new Dictionary<int, Group>();
            Group other = null;
            int paletteIndex = 0;

            foreach (int key in keys)
            {
                if (key == BlockedGroup || !byRegion || key >= 0 && paletteIndex < Palette.Length)
                {
                    colored[key] = groups[key];
                    if (key != BlockedGroup) paletteIndex++;
                    continue;
                }

                // Неизвестный регион (-1) или за пределами палитры
                other ??= new Group();
                Merge(other, groups[key]);
            }

            if (other != null)
                colored[OtherGroup] = other;

            var result = new List<Batch>();
            paletteIndex = 0;
            foreach (int key in keys)
            {
                if (!colored.TryGetValue(key, out var g)) continue;
                Color color = key == BlockedGroup ? BlockedColor : Palette[paletteIndex++ % Palette.Length];
                string label = key == BlockedGroup ? "заблокированные" : byRegion ? $"регион {key}" : "проходимые";
                result.Add(ToBatch(g, color, label));
            }
            if (other != null)
                result.Add(ToBatch(other, OtherColor, "мелкие регионы"));

            return result;
        }

        private static void Merge(Group into, Group from)
        {
            int offset = into.vertices.Count;
            into.vertices.AddRange(from.vertices);
            into.normals.AddRange(from.normals);
            foreach (int t in from.triangles) into.triangles.Add(t + offset);
            into.nodes += from.nodes;
        }

        private static Batch ToBatch(Group g, Color color, string label)
        {
            var mesh = new Mesh
            {
                name = "NavMesh3D Debug",
                indexFormat = g.vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16,
                hideFlags = HideFlags.HideAndDontSave
            };
            mesh.SetVertices(g.vertices);
            mesh.SetNormals(g.normals);
            mesh.SetTriangles(g.triangles, 0);
            mesh.RecalculateBounds();

            return new Batch { mesh = mesh, color = color, label = label, nodeCount = g.nodes };
        }
    }
}
