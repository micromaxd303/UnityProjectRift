using System.Collections.Generic;
using CaveGraphBuilder;
using UnityEngine;
using Random = System.Random;

/// <summary>
/// Превращает граф регионов (этап 1) в SDF-сцену (этап 2):
/// узлы — эллипсоиды со срезом пола, рёбра — кривые Catmull-Rom из цепочки капсул,
/// плюс штампы: ниши инкубаторов в гнёздах, укрытия на площадках, колонны в больших комнатах.
/// </summary>
public static class SdfSceneBuilder
{
    public static SdfScene Build(CaveGraph graph, SdfSceneSettings s, MovementConfig movement, int seed)
    {
        var rng = new Random(seed);
        var scene = new SdfScene(s, seed) { NodeCount = graph.Nodes.Count };

        // Высота пола каждой комнаты: к ней привязываются концы туннелей
        var floors = new float[graph.Nodes.Count];
        // Ровный пятачок площадки и хаба (радиус 0 у остальных комнат): туннели начинаются от его края
        var pads = new float[graph.Nodes.Count];
        var padCenters = new Vector3[graph.Nodes.Count];
        var roomRadii = new Vector3[graph.Nodes.Count];
        var shapes = new RoomShapes(scene, graph.Nodes.Count);
        for (int i = 0; i < graph.Nodes.Count; i++)
        {
            var node = graph.Nodes[i];
            padCenters[i] = node.Position;
            if (NeedsFlatFloor(node.Role))
            {
                pads[i] = node.Radius * s.padRadius;
                // Случайный сдвиг от центра, но так, чтобы пятачок оставался внутри комнаты
                float maxOffset = Mathf.Max(0f, Mathf.Min(node.Radius * s.padOffset, node.Radius * 0.9f - pads[i]));
                float angle = Range(rng, 0f, Mathf.PI * 2f);
                float dist = Range(rng, 0f, maxOffset);
                padCenters[i] += new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * dist;
            }
            shapes.First[i] = scene.Primitives.Count;
            floors[i] = AddRoom(scene, node, (ushort)(i + 1), pads[i], padCenters[i], s, rng, out roomRadii[i]);
            shapes.Count[i] = scene.Primitives.Count - shapes.First[i];
        }

        // Штампы идут после всех комнат и до туннелей, чтобы не разорвать цепочки капсул
        for (int i = 0; i < graph.Nodes.Count; i++)
            AddStamps(scene, shapes, graph, i, floors[i], roomRadii[i], pads[i], padCenters[i], s, rng);

        float crouch = movement != null ? movement.CrouchHeightMultiplier : 0.5f;
        // Высота туннеля от пола до потолка = r * (1 + floorCut); у лаза она равна росту в приседе + запас
        float crawlRadius = (s.playerHeight * crouch + s.crawlClearance) / (1f + s.tunnelFloorCut);

        for (int ei = 0; ei < graph.Edges.Count; ei++)
        {
            var e = graph.Edges[ei];
            var region = (ushort)(graph.Nodes.Count + ei + 1);
            float mask = s.tunnelNoise;

            switch (e.Type)
            {
                case EdgeType.Normal:
                    AddTunnel(scene, Port(graph, shapes, floors, pads, padCenters, e.A, e.B, s.tunnelRadius, s),
                        Port(graph, shapes, floors, pads, padCenters, e.B, e.A, s.tunnelRadius, s),
                        s.tunnelRadius, region, mask, s, rng);
                    break;

                case EdgeType.Crawl:
                    AddTunnel(scene, Port(graph, shapes, floors, pads, padCenters, e.A, e.B, crawlRadius, s),
                        Port(graph, shapes, floors, pads, padCenters, e.B, e.A, crawlRadius, s),
                        crawlRadius, region, mask, s, rng);
                    break;

                case EdgeType.Drop:
                    AddDrop(scene, shapes, graph, floors, pads, padCenters, e, region, mask, s, rng);
                    break;
            }
        }

        // Штампы ставились в координатах примитивов, а стены сдвинуты варпом: переводим точки в мир
        for (int i = 0; i < scene.Anchors.Count; i++)
        {
            var a = scene.Anchors[i];
            a.Position = scene.Unwarp(a.Position);
            scene.Anchors[i] = a;
        }

        return scene;
    }

    /// <summary>Эллипсоиды каждой комнаты (основной и лепестки): по ним ищется настоящий край пола.</summary>
    private sealed class RoomShapes
    {
        public readonly SdfScene Scene;
        public readonly int[] First, Count;

        public RoomShapes(SdfScene scene, int nodes)
        {
            Scene = scene;
            First = new int[nodes];
            Count = new int[nodes];
        }

        /// <summary>Пол комнаты над точкой (самый низкий из накрывающих её эллипсоидов) или NaN, если точка вне комнаты.</summary>
        public float FloorAt(int node, Vector3 p)
        {
            float floor = float.NaN;
            for (int i = First[node]; i < First[node] + Count[node]; i++)
            {
                var prim = Scene.Primitives[i];
                var q = new Vector3(p.x, prim.Floor + 0.5f, p.z);
                if (prim.Distance(q) < -0.25f && (float.IsNaN(floor) || prim.Floor < floor))
                    floor = prim.Floor;
            }
            return floor;
        }

        /// <summary>Расстояние по горизонтали от center до края пола комнаты в направлении dir (с учётом лепестков).</summary>
        public float Edge(int node, Vector3 center, Vector3 dir, float maxDistance)
        {
            float edge = 0f;
            for (float t = 0f; t <= maxDistance; t += 0.5f)
            {
                if (float.IsNaN(FloorAt(node, center + dir * t)))
                {
                    if (t > 0f) break;
                    continue;
                }
                edge = t;
            }
            return edge;
        }
    }

    /// <summary>
    /// Точка, от которой туннель выходит из комнаты. У площадки и хаба — край ровного пятачка в сторону соседа.
    /// У остальных — у края пола комнаты (с учётом лепестков), на радиус туннеля внутрь, а пол туннеля
    /// совпадает с полом комнаты именно в этом месте: лепесток может быть выше или ниже основной части.
    /// </summary>
    private static Vector3 Port(CaveGraph graph, RoomShapes shapes, float[] floors, float[] pads, Vector3[] padCenters,
        int node, int toward, float radius, SdfSceneSettings s)
    {
        Vector3 center = pads[node] > 0f ? padCenters[node] : graph.Nodes[node].Position;
        Vector3 dir = graph.Nodes[toward].Position - center;
        dir.y = 0f;
        dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.right;

        Vector3 p;
        float floor = floors[node];
        if (pads[node] > 0f)
            p = center + dir * pads[node];
        else
        {
            float edge = shapes.Edge(node, center, dir, graph.Nodes[node].Radius * 3f);
            p = center + dir * Mathf.Max(0f, edge - radius);
            float local = shapes.FloorAt(node, p);
            if (!float.IsNaN(local)) floor = local;
        }

        p.y = floor + radius * s.tunnelFloorCut;
        return p;
    }

    /// <summary>
    /// Добавляет комнату: основной эллипсоид и несколько лепестков вокруг него,
    /// слитых через smooth-min. У обычных комнат пол лепестков на разной высоте (уступы),
    /// у площадки и хаба пол общий, а в центре ровный пятачок. Возвращает высоту пола основной части.
    /// </summary>
    private static float AddRoom(SdfScene scene, CaveNode node, ushort region, float padRadius, Vector3 padCenter,
        SdfSceneSettings s, Random rng, out Vector3 radii)
    {
        float r = node.Radius;
        bool flat = padRadius > 0f;

        radii = new Vector3(
            r * (1f + Range(rng, -s.roomShapeJitter, s.roomShapeJitter)),
            r * s.roomHeightRatio,
            r * (1f + Range(rng, -s.roomShapeJitter, s.roomShapeJitter)));
        float floor = node.Position.y - radii.y * s.roomFloorCut;

        AddEllipsoid(scene, node.Position, radii, floor, padRadius, padCenter, region);

        int lobes = rng.Next(s.lobeCount.x, Mathf.Max(s.lobeCount.x, s.lobeCount.y) + 1);
        float angle = Range(rng, 0f, Mathf.PI * 2f);
        for (int k = 0; k < lobes; k++)
        {
            // Лепестки расходятся примерно равномерно по кругу, с разбросом
            angle += Mathf.PI * 2f / lobes * Range(rng, 0.7f, 1.3f);
            float dist = r * Range(rng, s.lobeOffset.x, s.lobeOffset.y);
            float lr = r * Range(rng, s.lobeRadius.x, s.lobeRadius.y);

            var lobeRadii = new Vector3(
                lr * (1f + Range(rng, -s.roomShapeJitter, s.roomShapeJitter)),
                lr * s.roomHeightRatio * (1f + Range(rng, -s.lobeHeightJitter, s.lobeHeightJitter)),
                lr * (1f + Range(rng, -s.roomShapeJitter, s.roomShapeJitter)));

            float lobeFloor = flat ? floor : floor + Range(rng, -s.lobeFloorStep, s.lobeFloorStep);
            var center = new Vector3(
                node.Position.x + Mathf.Cos(angle) * dist,
                lobeFloor + lobeRadii.y * s.roomFloorCut,
                node.Position.z + Mathf.Sin(angle) * dist);

            AddEllipsoid(scene, center, lobeRadii, lobeFloor, 0f, center, region);
        }

        return floor;
    }

    private static void AddEllipsoid(SdfScene scene, Vector3 center, Vector3 radii, float floor, float padRadius,
        Vector3 padCenter, ushort region)
    {
        scene.Add(new SdfPrimitive
        {
            Kind = SdfPrimitiveKind.Ellipsoid,
            A = center,
            Radii = radii,
            HasFloor = true,
            Floor = floor,
            FlatFloor = padRadius > 0f,
            PadRadius = padRadius,
            PadCenter = new Vector2(padCenter.x, padCenter.z),
            Region = region,
            NoiseMask = 1f,
            Bounds = SdfPrimitive.EllipsoidBounds(center, radii)
        });
    }

    // ---------- штампы ----------

    /// <summary>Ниши, укрытия и колонны одной комнаты. Всё, что поставлено, записывается в scene.Anchors.</summary>
    private static void AddStamps(SdfScene scene, RoomShapes shapes, CaveGraph graph, int node, float floor, Vector3 radii,
        float pad, Vector3 padCenter, SdfSceneSettings s, Random rng)
    {
        var n = graph.Nodes[node];
        var region = (ushort)(node + 1);

        // Откуда в комнату входят туннели: углы по горизонтали от центра пятачка (или комнаты)
        Vector3 origin = pad > 0f ? padCenter : n.Position;
        var tunnelDirs = new List<Vector3>();
        foreach (var e in graph.Edges)
        {
            if (e.A != node && e.B != node) continue;
            Vector3 dir = graph.Nodes[e.Other(node)].Position - origin;
            dir.y = 0f;
            if (dir.sqrMagnitude > 1e-6f) tunnelDirs.Add(dir.normalized);
        }

        if (pad > 0f)
            scene.Anchors.Add(new SdfAnchor
            {
                Kind = SdfAnchorKind.Pad,
                Position = new Vector3(padCenter.x, floor, padCenter.z),
                Forward = Vector3.forward,
                Node = node
            });

        var occupied = new List<(Vector3 pos, float radius)>();

        if (s.stampNiches && n.Role == LevelInfo.RegionRole.Nest)
            AddNiches(scene, shapes, n, node, region, floor, tunnelDirs, occupied, s, rng);

        if (s.stampCover && n.Role == LevelInfo.RegionRole.Platform && pad > 0f)
            AddCover(scene, node, floor, pad, padCenter, tunnelDirs, occupied, s, rng);

        if (s.stampPillars && n.Radius >= s.pillarMinRoomRadius)
            AddPillars(scene, n, node, floor, radii, pad, padCenter, tunnelDirs, occupied, s, rng);
    }

    /// <summary>
    /// Ниши под инкубаторы: полукупол в стене гнезда, пол вровень с полом комнаты в этом месте.
    /// Край пола ищется по настоящей форме комнаты (с лепестками), а не по основному эллипсоиду.
    /// Ставятся в стороны, где нет входов туннелей.
    /// </summary>
    private static void AddNiches(SdfScene scene, RoomShapes shapes, CaveNode n, int node, ushort region, float floor,
        List<Vector3> tunnelDirs, List<(Vector3, float)> occupied, SdfSceneSettings s, Random rng)
    {
        int count = rng.Next(s.nicheCount.x, Mathf.Max(s.nicheCount.x, s.nicheCount.y) + 1);
        var placed = new List<Vector3>();

        for (int k = 0; k < count; k++)
        {
            for (int attempt = 0; attempt < 24; attempt++)
            {
                float angle = Range(rng, 0f, Mathf.PI * 2f);
                var dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                if (!AngleFree(dir, tunnelDirs, 35f) || !AngleFree(dir, placed, 50f)) continue;

                float edge = shapes.Edge(node, n.Position, dir, n.Radius * 3f);
                float local = shapes.FloorAt(node, n.Position + dir * Mathf.Max(0f, edge - 0.5f));
                float nicheFloor = float.IsNaN(local) ? floor : local;

                Vector3 c = n.Position + dir * (edge + s.nicheDepth);
                c.y = nicheFloor;
                var radii = new Vector3(s.nicheRadius, s.nicheHeight, s.nicheRadius);

                scene.Add(new SdfPrimitive
                {
                    Kind = SdfPrimitiveKind.Ellipsoid,
                    A = c,
                    Radii = radii,
                    HasFloor = true,
                    Floor = nicheFloor,
                    Region = region,
                    NoiseMask = 0.25f,
                    Blend = s.stampBlend,
                    Stamp = SdfStampKind.Niche,
                    Bounds = SdfPrimitive.EllipsoidBounds(c, radii)
                });

                placed.Add(dir);
                occupied.Add((c, s.nicheRadius));
                // Точка инкубатора чуть ближе к комнате, чем центр купола, чтобы не упираться в заднюю стенку
                Vector3 anchor = c - dir * (s.nicheRadius * 0.4f);
                scene.Anchors.Add(new SdfAnchor { Kind = SdfAnchorKind.Incubator, Position = anchor, Forward = -dir, Node = node });
                break;
            }
        }
    }

    /// <summary>
    /// Укрытия на площадке: низкие каменные плиты по кругу вокруг места под ДУ,
    /// длинной стороной по касательной. Проходы к туннелям остаются свободными.
    /// </summary>
    private static void AddCover(SdfScene scene, int node, float floor, float pad, Vector3 padCenter,
        List<Vector3> tunnelDirs, List<(Vector3, float)> occupied, SdfSceneSettings s, Random rng)
    {
        int count = rng.Next(s.coverCount.x, Mathf.Max(s.coverCount.x, s.coverCount.y) + 1);
        float ring = pad * s.coverRing;
        float sink = 0.5f; // низ уходит в пол, чтобы под плитой не было щели
        float start = Range(rng, 0f, Mathf.PI * 2f);

        for (int k = 0; k < count; k++)
        {
            float angle = start + Mathf.PI * 2f * (k + Range(rng, -0.25f, 0.25f)) / count;
            var dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            float length = Range(rng, s.coverLength.x, s.coverLength.y);

            // Угловая ширина прохода к туннелю на этом радиусе
            float gap = Mathf.Atan2(s.tunnelRadius + length * 0.5f + 1f, ring) * Mathf.Rad2Deg;
            if (!AngleFree(dir, tunnelDirs, gap)) continue;

            var half = new Vector3(length * 0.5f, (s.coverHeight + sink) * 0.5f, s.coverThickness * 0.5f);
            Vector3 c = padCenter + dir * ring;
            c.y = floor + (s.coverHeight - sink) * 0.5f;

            scene.Add(new SdfPrimitive
            {
                Kind = SdfPrimitiveKind.Box,
                A = c,
                Radii = half,
                Yaw = angle + Mathf.PI * 0.5f, // длинной стороной по касательной к кругу
                Rounding = Mathf.Min(0.25f, half.z * 0.5f),
                Solid = true,
                Stamp = SdfStampKind.Cover,
                Bounds = SdfPrimitive.BoxBounds(c, half)
            });

            occupied.Add((c, half.x));
            scene.Anchors.Add(new SdfAnchor
            {
                Kind = SdfAnchorKind.Cover,
                Position = new Vector3(c.x, floor, c.z),
                Forward = -dir,
                Node = node
            });
        }
    }

    /// <summary>
    /// Колонны от пола до потолка в больших комнатах. Не ставятся на пятачок, в проходы
    /// туннелей, в ниши и слишком близко друг к другу.
    /// </summary>
    private static void AddPillars(SdfScene scene, CaveNode n, int node, float floor, Vector3 radii,
        float pad, Vector3 padCenter, List<Vector3> tunnelDirs, List<(Vector3 pos, float radius)> occupied,
        SdfSceneSettings s, Random rng)
    {
        int count = rng.Next(s.pillarCount.x, Mathf.Max(s.pillarCount.x, s.pillarCount.y) + 1);
        float spread = Mathf.Min(radii.x, radii.z) * 0.65f;
        float top = floor + radii.y * (1f + s.roomFloorCut) + 3f;

        for (int k = 0; k < count; k++)
        {
            for (int attempt = 0; attempt < 30; attempt++)
            {
                float pr = Range(rng, s.pillarRadius.x, s.pillarRadius.y);
                float a = Range(rng, 0f, Mathf.PI * 2f);
                float dist = spread * Mathf.Sqrt((float)rng.NextDouble());
                Vector3 c = n.Position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * dist;
                c.y = floor;

                if (!PillarFits(c, pr, n.Position, pad, padCenter, tunnelDirs, occupied, radii, s)) continue;

                var bottom = new Vector3(c.x, floor - 2f, c.z);
                var upper = new Vector3(c.x, top, c.z);
                scene.Add(new SdfPrimitive
                {
                    Kind = SdfPrimitiveKind.Capsule,
                    A = bottom,
                    B = upper,
                    Radii = new Vector3(pr, pr, pr),
                    Solid = true,
                    Stamp = SdfStampKind.Pillar,
                    Bounds = SdfPrimitive.CapsuleBounds(bottom, upper, pr)
                });

                occupied.Add((c, pr));
                scene.Anchors.Add(new SdfAnchor { Kind = SdfAnchorKind.Pillar, Position = c, Forward = Vector3.forward, Node = node });
                break;
            }
        }
    }

    private static bool PillarFits(Vector3 c, float pr, Vector3 roomCenter, float pad, Vector3 padCenter,
        List<Vector3> tunnelDirs, List<(Vector3 pos, float radius)> occupied, Vector3 radii, SdfSceneSettings s)
    {
        float free = pr + s.pillarClearance;

        if (pad > 0f && HorizontalDistance(c, padCenter) < pad + free)
            return false;

        foreach (var (pos, radius) in occupied)
            if (HorizontalDistance(c, pos) < radius + free)
                return false;

        // Туннели идут от центра комнаты (или пятачка) к соседям: держимся в стороне от этих лучей
        Vector3 origin = pad > 0f ? padCenter : roomCenter;
        float reach = Mathf.Max(radii.x, radii.z) * 1.2f;
        foreach (var dir in tunnelDirs)
            if (DistanceToRay(c, origin, dir, reach) < s.tunnelRadius + free)
                return false;

        return true;
    }

    private static bool AngleFree(Vector3 dir, List<Vector3> dirs, float minAngle)
    {
        float cos = Mathf.Cos(minAngle * Mathf.Deg2Rad);
        foreach (var d in dirs)
            if (Vector3.Dot(dir, d) > cos)
                return false;
        return true;
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

    private static float DistanceToRay(Vector3 p, Vector3 origin, Vector3 dir, float length)
    {
        var o = new Vector2(origin.x, origin.z);
        var d = new Vector2(dir.x, dir.z);
        var q = new Vector2(p.x, p.z);
        float t = Mathf.Clamp(Vector2.Dot(q - o, d), 0f, length);
        return (q - (o + d * t)).magnitude;
    }

    // Ровный пятачок: площадка под ДУ (GDD: «ровный пол, укрытия») и хаб под МППУ с раздатчиком
    private static bool NeedsFlatFloor(LevelInfo.RegionRole role) =>
        role is LevelInfo.RegionRole.Platform or LevelInfo.RegionRole.Hub;

    /// <summary>
    /// Обрыв: от верхней комнаты туннель идёт горизонтально до края нижней комнаты
    /// и обрывается уступом; если нижняя комната ниже конца туннеля, вниз ведёт шахта.
    /// </summary>
    private static void AddDrop(SdfScene scene, RoomShapes shapes, CaveGraph graph, float[] floors, float[] pads, Vector3[] padCenters,
        CaveEdge e, ushort region,
        float mask, SdfSceneSettings s, Random rng)
    {
        int upperIdx = graph.Nodes[e.A].Position.y >= graph.Nodes[e.B].Position.y ? e.A : e.B;
        int lowerIdx = e.Other(upperIdx);
        var upper = graph.Nodes[upperIdx];
        var lower = graph.Nodes[lowerIdx];
        Vector3 start = Port(graph, shapes, floors, pads, padCenters, upperIdx, lowerIdx, s.tunnelRadius, s);

        Vector3 toUpper = upper.Position - lower.Position;
        toUpper.y = 0f;
        Vector3 dir = toUpper.sqrMagnitude > 1e-6f ? toUpper.normalized : Vector3.right;

        Vector3 ledge = lower.Position + dir * (lower.Radius * 0.7f);
        ledge.y = start.y;

        AddTunnel(scene, start, ledge, s.tunnelRadius, region, mask, s, rng);

        // Низ шахты — на полу нижней комнаты, чтобы не продавить в нём яму
        Vector3 bottom = new(ledge.x, Mathf.Min(floors[lowerIdx] + s.tunnelRadius, ledge.y), ledge.z);
        AddCapsule(scene, ledge, bottom, s.tunnelRadius, hasFloor: false, 0f, region, mask);
    }

    /// <summary>Туннель между точками: кривая Catmull-Rom через два смещённых вбок узла, нарезанная на капсулы.</summary>
    private static void AddTunnel(SdfScene scene, Vector3 a, Vector3 b, float radius, ushort region, float mask,
        SdfSceneSettings s, Random rng)
    {
        Vector3 ab = b - a;
        float len = ab.magnitude;
        if (len < 1e-3f) return;

        Vector3 side = Vector3.Cross(Vector3.up, ab);
        side = side.sqrMagnitude > 1e-6f ? side.normalized : Vector3.right;

        Vector3 m1 = Vector3.Lerp(a, b, 1f / 3f) + side * (Range(rng, -1f, 1f) * s.tunnelWiggle * len);
        Vector3 m2 = Vector3.Lerp(a, b, 2f / 3f) + side * (Range(rng, -1f, 1f) * s.tunnelWiggle * len);

        // Концы продублированы, чтобы кривая проходила через a и b
        Vector3[] pts = { a, a, m1, m2, b, b };
        int perSpan = Mathf.Max(1, Mathf.CeilToInt(len / 3f / s.capsuleSpacing));

        Vector3 prev = a;
        for (int span = 0; span < 3; span++)
        {
            for (int k = 1; k <= perSpan; k++)
            {
                float t = k / (float)perSpan;
                Vector3 pt = CatmullRom(pts[span], pts[span + 1], pts[span + 2], pts[span + 3], t);
                AddCapsule(scene, prev, pt, radius, hasFloor: true, s.tunnelFloorCut, region, mask);
                prev = pt;
            }
        }
    }

    private static void AddCapsule(SdfScene scene, Vector3 a, Vector3 b, float radius, bool hasFloor, float floorCut,
        ushort region, float mask)
    {
        scene.Add(new SdfPrimitive
        {
            Kind = SdfPrimitiveKind.Capsule,
            A = a,
            B = b,
            Radii = new Vector3(radius, radius, radius),
            HasFloor = hasFloor,
            Floor = floorCut,
            Region = region,
            NoiseMask = mask,
            Bounds = SdfPrimitive.CapsuleBounds(a, b, radius)
        });
    }

    private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        return 0.5f * (2f * p1
                       + (p2 - p0) * t
                       + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2
                       + (3f * p1 - p0 - 3f * p2 + p3) * t3);
    }

    private static float Range(Random rng, float min, float max) => min + (float)rng.NextDouble() * (max - min);
}
