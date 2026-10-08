using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;

/// <summary>
/// Этап 7 пайплайна: разломы и точки начальных врагов. Работает по полю вокселей и анализу
/// из этапа 4, поэтому только для уровня, который прошёл проверку. Пишет прямо в LevelContext.
/// </summary>
public static class LevelPlacer
{
    private static readonly int[] DX = { 1, -1, 0, 0, 1, 1, -1, -1 };
    private static readonly int[] DZ = { 0, 0, 1, -1, 1, -1, 1, -1 };

    public static List<string> Place(LevelContext ctx, LevelValidationResult v, SdfScene scene,
        PlacementSettings s, int seed)
    {
        var log = new List<string>();
        var f = v.Field;
        var a = v.Analysis;
        var rules = v.Rules;
        var rng = new Random(seed * 7919 + 17);
        int nodeCount = ctx.Regions.Count;
        float voxel = f.VoxelSize;

        // Расстояние по полу от туннелей: многоисточниковый обход от клеток пола с меткой туннеля
        int[] tunnelDist = FloorDistanceFromTunnels(f, a.Standable, rules, nodeCount);
        int tunnelKeep = Mathf.CeilToInt(s.tunnelKeepAway / voxel);

        // Места, от которых держимся в стороне (в локальных координатах сцены)
        var blocked = new List<Vector3>();
        if (scene != null)
            foreach (var anchor in scene.Anchors)
                blocked.Add(anchor.Position);
        Vector3 spawnLocal = ctx.PlayerSpawn;

        int enemyCells = Mathf.Max(1, Mathf.CeilToInt(s.enemyHeight / voxel));
        // EDT считается до ближайшего сэмпла камня, а сама стена может быть ближе на долю вокселя
        float wallDistance = s.enemyRadius + s.wallMargin + voxel * 0.87f;
        float minRifts = Mathf.Max(1, ctx.MinRiftPointsPerRegion);

        // Клетки пола по комнатам
        var cells = new List<int>[nodeCount];
        for (int r = 0; r < nodeCount; r++) cells[r] = new List<int>();
        for (int i = 0; i < a.Standable.Length; i++)
        {
            if (!a.Standable[i] || !a.Reachable[i]) continue;
            int room = f.Labels[i] - 1;
            if (room >= 0 && room < nodeCount) cells[room].Add(i);
        }

        int totalRifts = 0, totalSpawns = 0;
        for (int r = 0; r < nodeCount; r++)
        {
            var region = ctx.Regions[r];
            var room = cells[r];
            float area = room.Count * voxel * voxel;

            // Кандидаты: ровный пол с местом под крупного врага, вдали от проходов, штампов и спавна
            var candidates = new List<int>();
            foreach (int i in room)
            {
                if (tunnelDist[i] < tunnelKeep) continue;
                Vector3 p = f.ToLocal(i);
                if (Near(p, blocked, s.anchorKeepAway)) continue;
                if (Horizontal(p, spawnLocal) < s.playerSpawnKeepAway && r == ctx.StartRegion) continue;
                if (!HasRoom(f, a.Edt, i, enemyCells, wallDistance)) continue;
                candidates.Add(i);
            }
            Shuffle(candidates, rng);

            // Разломы
            int wantRifts = Mathf.Min(s.maxRiftsPerRegion,
                Mathf.Max(Mathf.CeilToInt(minRifts), Mathf.FloorToInt(area / s.floorAreaPerRift)));
            var riftFloors = new List<Vector3>();
            // Пол под разломами и места попадания лучей: начальные враги туда не ставятся
            var riftArea = new List<Vector3>();
            foreach (int i in candidates)
            {
                if (region.Rifts.Count >= wantRifts) break;
                Vector3 floor = FloorPoint(f, i);
                if (Near(floor, riftFloors, s.riftSpacing)) continue;

                var rift = TryRift(f, a.Edt, i, floor, candidates, s, rng);
                if (rift == null) continue;

                riftFloors.Add(floor);
                riftArea.Add(floor);
                riftArea.AddRange(rift.GroundPoints);
                rift.Region = r;

                region.Rifts.Add(ctx.Rifts.Count);
                ctx.Rifts.Add(rift);
            }

            if (region.Rifts.Count < minRifts)
                log.Add($"комната {r} ({region.Role}): разломов {region.Rifts.Count} из {minRifts} — мало места (пол {area:F0} м²)");

            // Начальные враги
            bool skip = s.noSpawnsInStart && r == ctx.StartRegion;
            int wantSpawns = skip ? 0 : Mathf.Min(s.maxSpawnPointsPerRegion, Mathf.FloorToInt(area / s.floorAreaPerSpawn));
            var spawns = new List<Vector3>();
            // Свой порядок обхода: иначе первые кандидаты те же, что уже заняли разломы
            Shuffle(candidates, rng);
            foreach (int i in candidates)
            {
                if (spawns.Count >= wantSpawns) break;
                Vector3 floor = FloorPoint(f, i);
                if (Near(floor, spawns, s.spawnSpacing)) continue;
                if (Near(floor, riftArea, s.spawnRiftKeepAway)) continue;
                spawns.Add(floor);
                region.SpawnPoints.Add(floor);
            }

            AddMissionPoint(ctx, f, a, r, room, candidates, tunnelDist, enemyCells, s, rng, log);

            totalRifts += region.Rifts.Count;
            totalSpawns += region.SpawnPoints.Count;
        }

        int crystals = s.ceilingCrystals ? PlaceCeilingPoints(ctx, f, s, rng) : 0;

        log.Insert(0, $"разломов {totalRifts}, точек начальных врагов {totalSpawns}, точек кристаллов на потолке {crystals}");
        return log;
    }

    /// <summary>
    /// Точки под объекты миссий, у которых нет своего штампа:
    /// месторождение — места в стенах под кристаллы, тайник — самое укромное место (дальше всего от входов),
    /// логово, эвакуация и арена — самое просторное место комнаты.
    /// </summary>
    private static void AddMissionPoint(LevelContext ctx, VoxelField f, LevelAnalysis a, int r, List<int> room,
        List<int> candidates, int[] tunnelDist, int enemyCells, PlacementSettings s, Random rng, List<string> log)
    {
        var role = ctx.Regions[r].Role;
        var pool = candidates.Count > 0 ? candidates : room;
        if (pool.Count == 0) return;

        switch (role)
        {
            case LevelInfo.RegionRole.Deposit:
                int veins = AddVeins(ctx, f, a, r, tunnelDist, s, rng);
                if (veins < s.veinCount.x)
                    log.Add($"месторождение {r}: мест под кристаллы {veins} из {s.veinCount.x}");
                return;

            case LevelInfo.RegionRole.Cache:
            {
                int best = Best(pool, i => tunnelDist[i] == int.MaxValue ? 0 : tunnelDist[i]);
                AddAnchor(ctx, LevelAnchorKind.Item, FloorPoint(f, best), Vector3.forward, r, 0f);
                return;
            }

            case LevelInfo.RegionRole.Lair:
            case LevelInfo.RegionRole.Evacuation:
            case LevelInfo.RegionRole.Arena:
            {
                int best = Best(pool, i => BodyClearance(f, a.Edt, i, enemyCells));
                var kind = role switch
                {
                    LevelInfo.RegionRole.Lair => LevelAnchorKind.Lair,
                    LevelInfo.RegionRole.Evacuation => LevelAnchorKind.Evacuation,
                    _ => LevelAnchorKind.Arena
                };
                AddAnchor(ctx, kind, FloorPoint(f, best), Vector3.forward, r, BodyClearance(f, a.Edt, best, enemyCells));
                return;
            }
        }
    }

    /// <summary>
    /// Места в стенах месторождения: воздух у почти вертикальной стены на досягаемой высоте,
    /// под ним достижимый пол. Позиция — на поверхности стены, Forward — нормаль стены.
    /// </summary>
    private static int AddVeins(LevelContext ctx, VoxelField f, LevelAnalysis a, int r, int[] tunnelDist,
        PlacementSettings s, Random rng)
    {
        var label = (ushort)(r + 1);
        int maxDown = Mathf.CeilToInt(s.veinHeight.y / f.VoxelSize) + 1;
        float maxNormalY = Mathf.Cos(s.minVeinSteepness * Mathf.Deg2Rad);
        var cells = new List<int>();

        for (int z = 1; z < f.Size.z - 1; z++)
        for (int y = 1; y < f.Size.y - 1; y++)
        for (int x = 1; x < f.Size.x - 1; x++)
        {
            int i = f.Index(x, y, z);
            if (f.Labels[i] != label || !f.IsAir(i)) continue;
            if (f.IsAir(x + 1, y, z) && f.IsAir(x - 1, y, z) && f.IsAir(x, y, z + 1) && f.IsAir(x, y, z - 1)) continue;
            cells.Add(i);
        }
        Shuffle(cells, rng);

        int want = rng.Next(s.veinCount.x, Mathf.Max(s.veinCount.x, s.veinCount.y) + 1);
        var taken = new List<Vector3>();
        foreach (int i in cells)
        {
            if (taken.Count >= want) break;
            var c = f.Coord(i);
            if (a.Standable[i]) continue; // это пол, а не стена
            Vector3 n = SurfaceNormal(f, c);
            // Стены комнат у пола наклонные (чаша эллипсоида), поэтому берём всё круче minVeinSteepness,
            // кроме потолка: нормаль не должна смотреть вниз
            if (n.y > maxNormalY || n.y < -0.3f) continue;

            // Достижимый пол рядом: идём от стены в сторону комнаты (по нормали) и в каждой колонне
            // ищем клетку пола не ниже maxDown. Высота места считается от этого пола.
            if (!FloorNearWall(f, a, c, n, maxDown, out int floor)) continue;
            float height = (c.y - f.Coord(floor).y) * f.VoxelSize;
            if (height < s.veinHeight.x || height > s.veinHeight.y) continue;
            if (tunnelDist[floor] < 2) continue;

            Vector3 p = WallPoint(f, c, n);
            if (Near(p, taken, s.veinSpacing)) continue;

            taken.Add(p);
            AddAnchor(ctx, LevelAnchorKind.CrystalVein, p, n, r, 0f);
        }
        return taken.Count;
    }

    /// <summary>Первая достижимая клетка пола от стены в сторону комнаты (до 4 шагов по горизонтальной нормали).</summary>
    private static bool FloorNearWall(VoxelField f, LevelAnalysis a, Vector3Int c, Vector3 n, int maxDown, out int floor)
    {
        floor = -1;
        var dir = new Vector2(n.x, n.z);
        if (dir.sqrMagnitude < 1e-4f) return false;
        dir.Normalize();

        for (int k = 0; k <= 4; k++)
        {
            int x = Mathf.RoundToInt(c.x + dir.x * k), z = Mathf.RoundToInt(c.z + dir.y * k);
            if (x < 0 || z < 0 || x >= f.Size.x || z >= f.Size.z) return false;
            for (int y = c.y; y >= Mathf.Max(1, c.y - maxDown); y--)
            {
                int j = f.Index(x, y, z);
                if (!f.IsAir(j)) break;
                if (a.Standable[j] && a.Reachable[j])
                {
                    floor = j;
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>Точка на поверхности стены: от воздушного сэмпла к соседнему камню по оси, ближайшей к -n.</summary>
    private static Vector3 WallPoint(VoxelField f, Vector3Int c, Vector3 n)
    {
        Vector3Int axis = Mathf.Abs(n.x) >= Mathf.Abs(n.z)
            ? new Vector3Int(n.x > 0f ? -1 : 1, 0, 0)
            : new Vector3Int(0, 0, n.z > 0f ? -1 : 1);
        Vector3 p = f.ToLocal(c.x, c.y, c.z);
        var o = c + axis;
        if (!f.InBounds(o.x, o.y, o.z) || f.IsAir(o.x, o.y, o.z)) return p;

        float d0 = f.Density[f.Index(c.x, c.y, c.z)] - f.IsoLevel;
        float d1 = f.Density[f.Index(o.x, o.y, o.z)] - f.IsoLevel;
        float t = d1 - d0 > 1e-6f ? Mathf.Clamp01(-d0 / (d1 - d0)) : 0f;
        return p + (Vector3)axis * (t * f.VoxelSize);
    }

    private static float BodyClearance(VoxelField f, float[] edt, int i, int heightCells)
    {
        var c = f.Coord(i);
        float min = float.MaxValue;
        for (int y = c.y + 1; y <= Mathf.Min(f.Size.y - 1, c.y + heightCells); y++)
            min = Mathf.Min(min, edt[f.Index(c.x, y, c.z)]);
        return min == float.MaxValue ? 0f : min;
    }

    private static int Best(List<int> cells, System.Func<int, float> score)
    {
        int best = cells[0];
        float bestScore = float.MinValue;
        foreach (int i in cells)
        {
            float v = score(i);
            if (v > bestScore)
            {
                bestScore = v;
                best = i;
            }
        }
        return best;
    }

    /// <summary>Добавляет место и привязывает его к целям, в регионах которых оно стоит.</summary>
    private static void AddAnchor(LevelContext ctx, LevelAnchorKind kind, Vector3 p, Vector3 forward, int region, float radius)
    {
        int index = ctx.Anchors.Count;
        ctx.Anchors.Add(new LevelAnchor { Kind = kind, Position = p, Forward = forward, Region = region, Radius = radius });
        foreach (var o in ctx.Objectives)
            if (o.Regions.Contains(region))
                o.Anchors.Add(index);
    }

    /// <summary>
    /// Точки на потолке под кристаллы: воздух прямо под камнем, нормаль смотрит вниз не круче maxCeilingTilt,
    /// вокруг потолок почти плоский. Размер префаба ограничен плоским участком и местом под игрока.
    /// </summary>
    private static int PlaceCeilingPoints(LevelContext ctx, VoxelField f, PlacementSettings s, Random rng)
    {
        int nodeCount = ctx.Regions.Count;
        float voxel = f.VoxelSize;
        float minDown = Mathf.Cos(s.maxCeilingTilt * Mathf.Deg2Rad);

        var byLabel = new Dictionary<ushort, List<int>>();
        for (int z = 0; z < f.Size.z; z++)
        for (int y = 1; y < f.Size.y - 1; y++)
        for (int x = 0; x < f.Size.x; x++)
        {
            int i = f.Index(x, y, z);
            if (!f.IsAir(i) || f.IsAir(f.Index(x, y + 1, z))) continue;
            ushort label = f.Labels[i];
            if (label == 0) continue;
            if (!byLabel.TryGetValue(label, out var list))
                byLabel[label] = list = new List<int>();
            list.Add(i);
        }

        int placed = 0;
        foreach (var kv in byLabel)
        {
            bool room = kv.Key <= nodeCount;
            var cells = kv.Value;
            float perCrystal = s.ceilingAreaPerCrystal * (room ? 1f : s.tunnelCrystalRarity);
            int want = Mathf.FloorToInt(cells.Count * voxel * voxel / perCrystal);
            if (room) want = Mathf.Max(1, want);
            if (want == 0) continue;

            Shuffle(cells, rng);
            var taken = new List<Vector3>();
            foreach (int i in cells)
            {
                if (taken.Count >= want) break;

                Vector3 p = CeilingPoint(f, i);
                if (Near(p, taken, s.crystalSpacing)) continue;

                Vector3 n = SurfaceNormal(f, f.Coord(i));
                if (-n.y < minDown) continue;

                float radius = FlatRadius(f, p, n, s);
                if (radius < s.crystalRadius.x) continue;

                // До пола под точкой: кристалл не должен свисать игроку на голову
                float drop = DistanceDown(f, i);
                float maxLength = drop - s.headroomUnderCrystal;
                if (maxLength < s.crystalRadius.x) continue;

                taken.Add(p);
                ctx.CeilingPoints.Add(new LevelCeilingPoint
                {
                    Position = p,
                    Normal = n,
                    MaxRadius = radius,
                    MaxLength = maxLength,
                    Region = room ? kv.Key - 1 : -1,
                    Connection = room ? -1 : kv.Key - nodeCount - 1
                });
                placed++;
            }
        }
        return placed;
    }

    /// <summary>Точка на поверхности потолка: между воздухом в клетке и камнем над ней.</summary>
    private static Vector3 CeilingPoint(VoxelField f, int i)
    {
        var c = f.Coord(i);
        int above = f.Index(c.x, c.y + 1, c.z);
        float d0 = f.Density[i] - f.IsoLevel;     // < 0, воздух
        float d1 = f.Density[above] - f.IsoLevel; // > 0, камень
        float t = d1 - d0 > 1e-6f ? Mathf.Clamp01(-d0 / (d1 - d0)) : 0f;
        Vector3 p = f.ToLocal(i);
        p.y += t * f.VoxelSize;
        return p;
    }

    /// <summary>Нормаль поверхности по градиенту плотности, в сторону воздуха.</summary>
    private static Vector3 SurfaceNormal(VoxelField f, Vector3Int c)
    {
        var n = -new Vector3(
            D(f, c.x + 1, c.y, c.z) - D(f, c.x - 1, c.y, c.z),
            D(f, c.x, c.y + 1, c.z) - D(f, c.x, c.y - 1, c.z),
            D(f, c.x, c.y, c.z + 1) - D(f, c.x, c.y, c.z - 1));
        return n.sqrMagnitude > 1e-12f ? n.normalized : Vector3.down;
    }

    private static float D(VoxelField f, int x, int y, int z)
    {
        x = Mathf.Clamp(x, 0, f.Size.x - 1);
        y = Mathf.Clamp(y, 0, f.Size.y - 1);
        z = Mathf.Clamp(z, 0, f.Size.z - 1);
        return Mathf.Clamp(f.Density[f.Index(x, y, z)], -VoxelField.SolidDensity, VoxelField.SolidDensity);
    }

    /// <summary>
    /// Самый большой радиус (с шагом полвокселя), на котором потолок вокруг точки остаётся почти в плоскости
    /// с нормалью n: по 8 направлениям ищем поверхность потолка и сравниваем с плоскостью.
    /// </summary>
    private static float FlatRadius(VoxelField f, Vector3 p, Vector3 n, PlacementSettings s)
    {
        float step = f.VoxelSize * 0.5f;
        float best = 0f;
        for (float r = step; r <= s.crystalRadius.y + 1e-3f; r += step)
        {
            for (int d = 0; d < 8; d++)
            {
                float angle = d * Mathf.PI * 0.25f;
                // Направление в плоскости потолка
                Vector3 dir = Vector3.ProjectOnPlane(new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)), n).normalized;
                Vector3 q = p + dir * r;
                if (!CeilingHeightAt(f, q, out float y)) return best;
                // Отклонение от плоскости по вертикали, пересчитанное на нормаль
                if (Mathf.Abs((y - q.y) * n.y) > s.ceilingFlatness) return best;
            }
            best = r;
        }
        return best;
    }

    /// <summary>Высота потолка в колонне рядом с точкой: ищем переход воздух → камень около её высоты.</summary>
    private static bool CeilingHeightAt(VoxelField f, Vector3 q, out float y)
    {
        y = 0f;
        Vector3Int v = f.FromLocal(q);
        int y0 = Mathf.Max(0, v.y - 2), y1 = Mathf.Min(f.Size.y - 2, v.y + 2);
        for (int yy = y0; yy <= y1; yy++)
        {
            int i = f.Index(v.x, yy, v.z);
            if (f.IsAir(i) && !f.IsAir(f.Index(v.x, yy + 1, v.z)))
            {
                y = CeilingPoint(f, i).y;
                return true;
            }
        }
        return false;
    }

    private static float DistanceDown(VoxelField f, int i)
    {
        var c = f.Coord(i);
        int y = c.y;
        while (y > 0 && f.IsAir(f.Index(c.x, y - 1, c.z))) y--;
        return (c.y - y + 1) * f.VoxelSize;
    }

    /// <summary>
    /// Разлом над клеткой пола: в воздухе на случайной высоте, со свободным местом вокруг,
    /// и хотя бы одна точка на полу в радиусе лучей, куда виден прямой луч.
    /// </summary>
    private static LevelRift TryRift(VoxelField f, float[] edt, int floorCell, Vector3 floor, List<int> candidates,
        PlacementSettings s, Random rng)
    {
        float h = s.riftHeight.x + (float)rng.NextDouble() * (s.riftHeight.y - s.riftHeight.x);
        Vector3 p = floor + Vector3.up * h;

        Vector3Int v = f.FromLocal(p);
        int pi = f.Index(v.x, v.y, v.z);
        if (!f.IsAir(pi) || edt[pi] < s.riftClearance) return null;
        // От пола до разрыва и на запас выше — воздух
        var c = f.Coord(floorCell);
        for (int y = c.y; y <= Mathf.Min(f.Size.y - 1, v.y + 1); y++)
            if (!f.IsAir(f.Index(c.x, y, c.z)))
                return null;

        var rift = new LevelRift { Position = p, FloorBelow = floor };
        foreach (int j in candidates)
        {
            if (rift.GroundPoints.Count >= s.groundPointsPerRift) break;
            Vector3 g = FloorPoint(f, j);
            float d = Horizontal(g, floor);
            if (d < s.beamRange.x || d > s.beamRange.y) continue;
            if (Mathf.Abs(g.y - floor.y) > 3f) continue;
            if (Near(g, rift.GroundPoints, s.groundPointSpacing)) continue;
            if (!LineOfSight(f, p, g + Vector3.up * 0.5f)) continue;
            rift.GroundPoints.Add(g);
        }

        return rift.GroundPoints.Count > 0 ? rift : null;
    }

    /// <summary>
    /// Над клеткой пола свободно на высоту врага, и по всей высоте корпуса (кроме самого пола)
    /// до стены не ближе wallDistance. Стены пещеры наклонные, поэтому проверяется каждый уровень.
    /// </summary>
    private static bool HasRoom(VoxelField f, float[] edt, int i, int heightCells, float wallDistance)
    {
        var c = f.Coord(i);
        if (c.y + heightCells >= f.Size.y) return false;
        for (int y = c.y; y <= c.y + heightCells; y++)
            if (!f.IsAir(f.Index(c.x, y, c.z)))
                return false;

        // От пола EDT на высоте y не больше y - c.y + 1 вокселя, поэтому низ корпуса сверяем с этой поправкой
        for (int y = c.y + 1; y <= c.y + heightCells; y++)
        {
            float need = Mathf.Min(wallDistance, (y - c.y + 1) * f.VoxelSize - 0.01f);
            if (edt[f.Index(c.x, y, c.z)] < need)
                return false;
        }
        return true;
    }

    private static bool LineOfSight(VoxelField f, Vector3 from, Vector3 to)
    {
        float len = Vector3.Distance(from, to);
        int steps = Mathf.Max(1, Mathf.CeilToInt(len / (f.VoxelSize * 0.5f)));
        for (int k = 1; k < steps; k++)
        {
            Vector3Int v = f.FromLocal(Vector3.Lerp(from, to, k / (float)steps));
            if (!f.IsAir(f.Index(v.x, v.y, v.z)))
                return false;
        }
        return true;
    }

    /// <summary>Точка на поверхности пола: между камнем под клеткой и воздухом в ней.</summary>
    private static Vector3 FloorPoint(VoxelField f, int i)
    {
        var c = f.Coord(i);
        Vector3 p = f.ToLocal(i);
        int below = f.Index(c.x, c.y - 1, c.z);
        float d0 = f.Density[below] - f.IsoLevel;
        float d1 = f.Density[i] - f.IsoLevel;
        float t = d0 - d1 > 1e-6f ? Mathf.Clamp01(d0 / (d0 - d1)) : 1f;
        p.y = f.ToLocal(c.x, c.y - 1, c.z).y + t * f.VoxelSize;
        return p;
    }

    /// <summary>Шаги по полу (в вокселях) до ближайшей клетки туннеля. int.MaxValue — туннель недостижим.</summary>
    private static int[] FloorDistanceFromTunnels(VoxelField f, bool[] standable, PlayerRules r, int nodeCount)
    {
        var dist = new int[f.Count];
        var queue = new Queue<int>();
        for (int i = 0; i < dist.Length; i++)
        {
            dist[i] = int.MaxValue;
            if (standable[i] && f.Labels[i] > nodeCount)
            {
                dist[i] = 0;
                queue.Enqueue(i);
            }
        }

        while (queue.Count > 0)
        {
            int cur = queue.Dequeue();
            var c = f.Coord(cur);
            for (int d = 0; d < 8; d++)
            {
                int nx = c.x + DX[d], nz = c.z + DZ[d];
                if (nx < 0 || nz < 0 || nx >= f.Size.x || nz >= f.Size.z) continue;
                for (int ny = Mathf.Max(1, c.y - r.JumpUp); ny <= Mathf.Min(f.Size.y - 1, c.y + r.JumpUp); ny++)
                {
                    int j = f.Index(nx, ny, nz);
                    if (!standable[j] || dist[j] != int.MaxValue) continue;
                    dist[j] = dist[cur] + 1;
                    queue.Enqueue(j);
                }
            }
        }
        return dist;
    }

    private static bool Near(Vector3 p, List<Vector3> points, float distance)
    {
        foreach (var q in points)
            if (Horizontal(p, q) < distance && Mathf.Abs(p.y - q.y) < 3f)
                return true;
        return false;
    }

    private static float Horizontal(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

    private static void Shuffle(List<int> list, Random rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
