using System;
using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;

namespace CaveGraphBuilder
{
    public class CaveGraphResult
    {
        public bool Success;
        public int Attempts;
        /// <summary>Граф удачной попытки, при неудаче — граф последней попытки (для отладки).</summary>
        public CaveGraph Graph;
        /// <summary>Все рёбра Делоне последней попытки (для гизмо).</summary>
        public List<(int a, int b)> DelaunayEdges;
        public readonly List<string> Log = new();
    }

    /// <summary>
    /// Этап 1 пайплайна: строит граф регионов, который удовлетворяет требованиям миссий.
    /// Mitchell → Делоне → Kruskal → петли → роли → ремонт связей → проверка.
    /// При отказе повторяет с новым подсидом.
    /// </summary>
    public class CaveGraphGenerator
    {
        private readonly GraphBuilderConfig _config;
        private readonly CaveGraphSettings _s;
        private readonly float _jumpHeight;
        private readonly float _walkSpeed;

        private LevelInfo.LevelRequirements _req;
        private CaveGraph _graph;
        private Random _rng;
        private List<(int a, int b)> _delaunay;
        private List<(int a, int b)> _gabriel;
        private readonly Dictionary<int, LevelInfo.RegionRequirement> _nodeReq = new();
        private readonly List<string> _warnings = new();

        public CaveGraphGenerator(GraphBuilderConfig config)
        {
            _config = config;
            _s = config.caveGraphSettings ?? new CaveGraphSettings();

            var m = config.movement;
            _walkSpeed = m != null ? m.WalkSpeed : 10f;
            // BaseJumpForce считаем начальной скоростью прыжка: h = v^2 / 2g
            _jumpHeight = m != null && m.Gravity > 0f ? m.BaseJumpForce * m.BaseJumpForce / (2f * m.Gravity) : 2f;
        }

        public CaveGraphResult Generate(LevelInfo.LevelRequirements requirements, Vector2 borderDelta, int seed)
        {
            var result = new CaveGraphResult();
            _req = requirements;

            int nodeCount = _config.mitchelSamplerSettings.desiredNodesCount;
            int needed = requirements.MinRegionCount();
            if (nodeCount < needed)
            {
                result.Log.Add($"Узлов {nodeCount}, а требованиям нужно минимум {needed} регионов. Увеличь desiredNodesCount.");
                return result;
            }

            for (int attempt = 0; attempt < _s.maxAttempts; attempt++)
            {
                int subSeed = unchecked(seed * 73856093 ^ (attempt + 1) * 19349663);
                _rng = new Random(subSeed);
                _nodeReq.Clear();
                _warnings.Clear();
                _graph = null;

                string reason;
                try
                {
                    reason = TryBuild(borderDelta, subSeed);
                }
                catch (Exception ex)
                {
                    reason = $"исключение: {ex.Message}";
                }

                result.Attempts = attempt + 1;
                if (_graph != null)
                {
                    result.Graph = _graph;
                    result.DelaunayEdges = _delaunay;
                }

                if (reason == null)
                {
                    result.Success = true;
                    result.Log.Add($"Попытка {attempt + 1} (подсид {subSeed}): успех, " +
                                   $"{_graph.Nodes.Count} узлов, {_graph.Edges.Count} рёбер, " +
                                   $"длительность ~{_graph.EstimatedDuration:F0} с");
                    foreach (var w in _warnings)
                        result.Log.Add("  предупреждение: " + w);
                    return result;
                }

                result.Log.Add($"Попытка {attempt + 1} (подсид {subSeed}): {reason}");
            }

            return result;
        }

        // ------------------------------------------------------------------

        private string TryBuild(Vector2 borderDelta, int subSeed)
        {
            var positions = new MitchelSampler(_config.mitchelSamplerSettings, subSeed).GenerateNode(borderDelta);

            // DivideConquer сортирует список на месте: индексы ниже относятся к отсортированному порядку
            var triangulation = new DivideConquer(positions);
            _gabriel = triangulation.Generate();
            _delaunay = triangulation.GetEdges();
            _graph = new CaveGraph(positions);

            if (!BuildSpanningTree())
                return "после фильтра по уклону граф несвязен";

            ChooseStart();
            AddLoops(LoopTarget(_req.Loops), _gabriel, allowDrops: true);
            _graph.UpdateDistances(_s.tunnelWinding);

            string reason = AssignRoles();
            if (reason != null) return reason;

            reason = RepairConnections();
            if (reason != null) return reason;

            _graph.UpdateDistances(_s.tunnelWinding);

            reason = Validate();
            if (reason != null) return reason;

            _graph.EstimatedDuration = EstimateDuration();
            var target = _req.TargetDurationSeconds;
            if (_graph.EstimatedDuration < target.x || _graph.EstimatedDuration > target.y)
            {
                string msg = $"длительность ~{_graph.EstimatedDuration:F0} с вне диапазона {target.x:F0}–{target.y:F0} с";
                if (_s.rejectOnDuration) return msg;
                _warnings.Add(msg);
            }

            return null;
        }

        // ---------- рёбра ----------

        /// <summary>Тип туннеля между узлами или null, если соединить нельзя.</summary>
        private EdgeType? Classify(int a, int b, out float slopeDeg)
        {
            Vector3 pa = _graph.Nodes[a].Position, pb = _graph.Nodes[b].Position;
            float horizontal = new Vector2(pa.x - pb.x, pa.z - pb.z).magnitude;
            float dh = Mathf.Abs(pa.y - pb.y);
            slopeDeg = Mathf.Atan2(dh, horizontal) * Mathf.Rad2Deg;

            // Пологий туннель или уступ, на который можно запрыгнуть
            if (slopeDeg <= _s.maxTunnelSlope || dh <= _jumpHeight)
                return EdgeType.Normal;
            if (dh <= _s.maxDropHeight)
                return EdgeType.Drop;
            return null;
        }

        /// <summary>Kruskal по рёбрам Делоне с весом «длина + штраф за уклон». Обрывы в остов не идут.</summary>
        private bool BuildSpanningTree()
        {
            var weighted = new List<(float w, int a, int b)>();
            foreach (var (a, b) in _delaunay)
            {
                if (Classify(a, b, out float slope) != EdgeType.Normal) continue;
                float len = Vector3.Distance(_graph.Nodes[a].Position, _graph.Nodes[b].Position);
                float penalty = _s.maxTunnelSlope > 0f ? Mathf.Clamp01(slope / _s.maxTunnelSlope) : 0f;
                weighted.Add((len * (1f + _s.slopePenalty * penalty), a, b));
            }
            weighted.Sort((x, y) => x.w.CompareTo(y.w));

            int n = _graph.Nodes.Count;
            var parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = i;

            int Find(int x)
            {
                while (parent[x] != x)
                {
                    parent[x] = parent[parent[x]];
                    x = parent[x];
                }
                return x;
            }

            int added = 0;
            foreach (var (_, a, b) in weighted)
            {
                int ra = Find(a), rb = Find(b);
                if (ra == rb) continue;
                parent[ra] = rb;
                _graph.AddEdge(a, b, EdgeType.Normal, isLoop: false);
                if (++added == n - 1) break;
            }

            return added == n - 1;
        }

        private int LoopTarget(LevelInfo.LoopAmount amount)
        {
            float fraction = amount switch
            {
                LevelInfo.LoopAmount.Few => _s.loopsFew,
                LevelInfo.LoopAmount.Some => _s.loopsSome,
                LevelInfo.LoopAmount.Many => _s.loopsMany,
                _ => _s.loopsAny
            };
            return Mathf.RoundToInt(fraction * _graph.Nodes.Count);
        }

        /// <summary>
        /// Добавляет до count петель из кандидатов: каждый раз ребро с самым большим
        /// отношением «путь по графу / прямая», пока оно не меньше minLoopRatio.
        /// </summary>
        private void AddLoops(int count, List<(int a, int b)> candidates, bool allowDrops)
        {
            var pool = new List<(int a, int b, EdgeType type)>();
            foreach (var (a, b) in candidates)
            {
                if (_graph.HasEdge(a, b) || IsLocked(a) || IsLocked(b)) continue;
                var type = Classify(a, b, out _);
                if (type == null || (type == EdgeType.Drop && !allowDrops)) continue;
                pool.Add((a, b, type.Value));
            }

            for (int k = 0; k < count && pool.Count > 0; k++)
            {
                int best = -1;
                float bestRatio = 0f;
                var cache = new Dictionary<int, float[]>();

                for (int i = 0; i < pool.Count; i++)
                {
                    var (a, b, _) = pool[i];
                    if (!cache.TryGetValue(a, out var dist))
                        cache[a] = dist = _graph.Dijkstra(a, 1f, walkOnly: true);

                    float euclid = Vector3.Distance(_graph.Nodes[a].Position, _graph.Nodes[b].Position);
                    float ratio = euclid > 0f ? dist[b] / euclid : 0f;
                    if (ratio > bestRatio)
                    {
                        bestRatio = ratio;
                        best = i;
                    }
                }

                if (best < 0 || bestRatio < _s.minLoopRatio) break;

                var (ea, eb, etype) = pool[best];
                if (etype == EdgeType.Normal)
                {
                    // Пологий туннель с перепадом выше прыжка можно сделать обрывом:
                    // туннель идёт ровно и заканчивается уступом, обратно не забраться
                    float dh = Mathf.Abs(_graph.Nodes[ea].Position.y - _graph.Nodes[eb].Position.y);
                    if (allowDrops && dh > _jumpHeight && dh <= _s.maxDropHeight && _rng.NextDouble() < _s.dropChance)
                        etype = EdgeType.Drop;
                    else if (_rng.NextDouble() < _s.crawlChance)
                        etype = EdgeType.Crawl;
                }
                _graph.AddEdge(ea, eb, etype, isLoop: true);
                pool.RemoveAt(best);
            }
        }

        // ---------- старт и роли ----------

        /// <summary>Старт — один из трёх узлов, самых далёких от центра (на краю уровня).</summary>
        private void ChooseStart()
        {
            Vector3 c = Vector3.zero;
            foreach (var node in _graph.Nodes) c += node.Position;
            c /= _graph.Nodes.Count;

            var order = new List<int>();
            for (int i = 0; i < _graph.Nodes.Count; i++) order.Add(i);
            order.Sort((x, y) => XZDist(_graph.Nodes[y].Position, c).CompareTo(XZDist(_graph.Nodes[x].Position, c)));

            int start = order[_rng.Next(Mathf.Min(3, order.Count))];
            _graph.Start = start;

            var startNode = _graph.Nodes[start];
            startNode.Role = LevelInfo.RegionRole.Start;
            startNode.Radius = RandomRadius(LevelInfo.RegionSize.Medium);

            foreach (var r in _req.Regions)
                if (r.Role == LevelInfo.RegionRole.Start)
                    _nodeReq[start] = r;
        }

        private string AssignRoles()
        {
            // Сначала основная миссия, внутри — сначала требования с обязательными связями
            var order = new List<LevelInfo.RegionRequirement>();
            foreach (var r in _req.Regions)
                if (r.Role != LevelInfo.RegionRole.Start && !r.IsSideMission && r.ConnectionsRequired) order.Add(r);
            foreach (var r in _req.Regions)
                if (r.Role != LevelInfo.RegionRole.Start && !r.IsSideMission && !r.ConnectionsRequired) order.Add(r);
            foreach (var r in _req.Regions)
                if (r.Role != LevelInfo.RegionRole.Start && r.IsSideMission) order.Add(r);

            float maxDist = MaxStartDistance();

            foreach (var req in order)
            {
                int want = _rng.Next(req.MinCount, Mathf.Max(req.MinCount, req.MaxCount) + 1);
                if (want == 0) continue;

                var candidates = new List<(float score, int node)>();
                for (int i = 0; i < _graph.Nodes.Count; i++)
                {
                    var node = _graph.Nodes[i];
                    if (node.Role != LevelInfo.RegionRole.None) continue;
                    if (node.Distance01 < req.MinDistance - 1e-4f || node.Distance01 > req.MaxDistance + 1e-4f) continue;
                    if (req.DeadEnd && req.ConnectionsRequired && _graph.Degree(i) != 1) continue;

                    candidates.Add((Score(i, req), i));
                }
                candidates.Sort((x, y) => y.score.CompareTo(x.score));

                var chosen = new List<int>();
                foreach (var (_, node) in candidates)
                {
                    if (chosen.Count >= want) break;
                    if (req.MinSeparation > 0f && !FarEnough(node, chosen, req.MinSeparation, maxDist)) continue;
                    chosen.Add(node);
                }

                if (chosen.Count < req.MinCount)
                    return $"{RoleName(req)}: подходящих регионов {chosen.Count} из {req.MinCount}";

                foreach (int i in chosen)
                {
                    var node = _graph.Nodes[i];
                    node.Role = req.Role;
                    node.SideMissionId = req.SideMissionId;
                    node.Radius = RandomRadius(req.Size);
                    _nodeReq[i] = req;
                }
            }

            // Обычные регионы
            foreach (var node in _graph.Nodes)
                if (node.Radius <= 0f)
                    node.Radius = RandomRadius(_rng.NextDouble() < 0.5 ? LevelInfo.RegionSize.Small : LevelInfo.RegionSize.Medium);

            return null;
        }

        private float Score(int i, LevelInfo.RegionRequirement req)
        {
            var bridges = _graph.FindBridges();
            int degree = _graph.Degree(i);
            float score = 0f;

            if (req.DeadEnd) score += degree == 1 ? 2f : -2f;
            if (degree >= req.MinEntrances) score += 1f;
            if (req.MaxEntrances > 0 && degree > req.MaxEntrances) score -= 2f;
            if (req.OnLoop && _graph.IsOnLoop(i, bridges)) score += 1f;
            if (req.MinDisjointPaths >= 2 && _graph.HasTwoDisjointPaths(i, bridges)) score += 1f;

            // Регион «рядом со стартом» — чем ближе, тем лучше, иначе — чем дальше
            float d = _graph.Nodes[i].Distance01;
            score += req.MaxDistance < 1f ? (1f - d) * 0.5f : d * 0.5f;

            return score + (float)_rng.NextDouble() * 0.3f;
        }

        private bool FarEnough(int node, List<int> chosen, float minSeparation, float maxDist)
        {
            if (chosen.Count == 0 || maxDist <= 0f) return true;
            var dist = _graph.Dijkstra(node, _s.tunnelWinding);
            foreach (int other in chosen)
                if (dist[other] / maxDist < minSeparation)
                    return false;
            return true;
        }

        // ---------- ремонт связей ----------

        /// <summary>
        /// Для регионов с обязательными связями добавляет рёбра: недостающие входы,
        /// петлю, второй путь от старта. Регионы-тупики не трогаются.
        /// </summary>
        private string RepairConnections()
        {
            foreach (var (node, req) in _nodeReq)
            {
                if (!req.ConnectionsRequired || req.DeadEnd) continue;

                while (_graph.Degree(node) < req.MinEntrances)
                {
                    if (!AddRepairEdge(node, _ => true))
                        return $"{RoleName(req)}: не хватает входов ({_graph.Degree(node)} из {req.MinEntrances})";
                }

                if (req.OnLoop && !_graph.IsOnLoop(node, _graph.FindBridges()))
                {
                    if (!AddRepairEdge(node, n => _graph.IsOnLoop(n, _graph.FindBridges())))
                        return $"{RoleName(req)}: не удалось поставить на петлю";
                }

                if (req.MinDisjointPaths >= 2 && !_graph.HasTwoDisjointPaths(node, _graph.FindBridges()))
                {
                    if (!AddRepairEdge(node, n => _graph.HasTwoDisjointPaths(n, _graph.FindBridges())))
                        return $"{RoleName(req)}: не удалось проложить второй путь";
                }
            }

            return null;
        }

        /// <summary>
        /// Пробует рёбра Делоне от узла, от коротких к длинным, и оставляет первое,
        /// после которого выполняется условие.
        /// </summary>
        private bool AddRepairEdge(int node, Func<int, bool> condition)
        {
            var options = new List<(float len, int other, EdgeType type)>();
            foreach (var (a, b) in _delaunay)
            {
                if (a != node && b != node) continue;
                int other = a == node ? b : a;
                if (_graph.HasEdge(node, other) || IsLocked(other) || IsFull(other)) continue;

                var type = Classify(node, other, out _);
                if (type != EdgeType.Normal) continue;

                float len = Vector3.Distance(_graph.Nodes[node].Position, _graph.Nodes[other].Position);
                options.Add((len, other, type.Value));
            }
            options.Sort((x, y) => x.len.CompareTo(y.len));

            foreach (var (_, other, type) in options)
            {
                _graph.AddEdge(node, other, type, isLoop: true);
                if (condition(node)) return true;
                _graph.RemoveLastEdge();
            }

            return false;
        }

        /// <summary>Регион-тупик: к нему нельзя добавлять рёбра.</summary>
        private bool IsLocked(int node) =>
            _nodeReq.TryGetValue(node, out var r) && r.DeadEnd && _graph.Degree(node) <= 1;

        /// <summary>У региона уже максимум входов.</summary>
        private bool IsFull(int node) =>
            _nodeReq.TryGetValue(node, out var r) && r.MaxEntrances > 0 && _graph.Degree(node) >= r.MaxEntrances;

        // ---------- проверка ----------

        private string Validate()
        {
            var bridges = _graph.FindBridges();
            float maxDist = MaxStartDistance();

            for (int i = 0; i < _graph.Nodes.Count; i++)
                if (float.IsInfinity(_graph.Nodes[i].Distance))
                    return $"узел {i} недостижим от старта";

            var groups = new Dictionary<LevelInfo.RegionRequirement, List<int>>();
            foreach (var (node, req) in _nodeReq)
            {
                if (!groups.TryGetValue(req, out var list))
                    groups[req] = list = new List<int>();
                list.Add(node);
            }

            foreach (var req in _req.Regions)
            {
                groups.TryGetValue(req, out var nodes);
                nodes ??= new List<int>();
                string name = RoleName(req);

                // Требования к самому региону обязательны всегда
                if (nodes.Count < req.MinCount)
                    return $"{name}: регионов {nodes.Count} из {req.MinCount}";

                foreach (int i in nodes)
                {
                    float d = _graph.Nodes[i].Distance01;
                    if (d < req.MinDistance - 1e-4f || d > req.MaxDistance + 1e-4f)
                        return $"{name}: расстояние {d:F2} вне [{req.MinDistance:F2}, {req.MaxDistance:F2}] после ремонта связей";
                }

                if (req.MinSeparation > 0f)
                    foreach (int i in nodes)
                        if (!FarEnough(i, nodes.FindAll(o => o != i), req.MinSeparation, maxDist))
                            return $"{name}: регионы ближе {req.MinSeparation:F2} друг к другу";

                // Требования к связям: обязательные — отказ, желательные — предупреждение
                foreach (int i in nodes)
                {
                    int degree = _graph.Degree(i);
                    string problem = null;
                    if (req.DeadEnd && degree != 1) problem = "не тупик";
                    else if (degree < req.MinEntrances) problem = $"входов {degree} из {req.MinEntrances}";
                    else if (req.MaxEntrances > 0 && degree > req.MaxEntrances) problem = $"входов {degree}, максимум {req.MaxEntrances}";
                    else if (req.OnLoop && !_graph.IsOnLoop(i, bridges)) problem = "не на петле";
                    else if (req.MinDisjointPaths >= 2 && !_graph.HasTwoDisjointPaths(i, bridges)) problem = "нет второго пути от старта";

                    if (problem == null) continue;
                    if (req.ConnectionsRequired) return $"{name}: {problem}";
                    _warnings.Add($"{name}: {problem} (желательное требование)");
                }
            }

            return null;
        }

        // ---------- длительность ----------

        private float EstimateDuration()
        {
            float speed = Mathf.Max(0.1f, _walkSpeed * _s.travelEfficiency);
            float w = _s.tunnelWinding;
            var mainTargets = new List<int>();
            for (int i = 0; i < _graph.Nodes.Count; i++)
            {
                var n = _graph.Nodes[i];
                if (n.Role != LevelInfo.RegionRole.None && n.Role != LevelInfo.RegionRole.Start && string.IsNullOrEmpty(n.SideMissionId))
                    mainTargets.Add(i);
            }

            float seconds = 0f;
            switch (_req.Duration)
            {
                case LevelInfo.DurationModel.Exploration:
                    seconds = TourLength(mainTargets, returnToStart: true) / speed
                              + _s.fightSecondsPerObjective * mainTargets.Count;
                    break;

                case LevelInfo.DurationModel.Shuttle:
                {
                    var hubs = _graph.NodesWithRole(LevelInfo.RegionRole.Hub);
                    var deposits = _graph.NodesWithRole(LevelInfo.RegionRole.Deposit);
                    if (hubs.Count == 0 || deposits.Count == 0) break;

                    var fromHub = _graph.Dijkstra(hubs[0], w);
                    float lap = 0f;
                    foreach (int d in deposits)
                    {
                        // туда и обратно; обратный путь может идти другой дорогой
                        var fromDeposit = _graph.Dijkstra(d, w);
                        lap += fromHub[d] + fromDeposit[hubs[0]];
                    }
                    lap /= deposits.Count;

                    int trips = Mathf.CeilToInt(_s.crystalsRequired / (float)Mathf.Max(1, _s.carryLimit));
                    seconds = (_graph.Nodes[hubs[0]].Distance + trips * lap) / speed;
                    break;
                }

                case LevelInfo.DurationModel.Defense:
                    seconds = TourLength(mainTargets, returnToStart: false) / speed
                              + _s.defenseSecondsPerPlatform * mainTargets.Count;
                    break;

                case LevelInfo.DurationModel.Arena:
                    foreach (int i in mainTargets)
                        seconds = Mathf.Max(seconds, _graph.Nodes[i].Distance / speed);
                    seconds += _s.arenaSeconds;
                    break;

                case LevelInfo.DurationModel.Run:
                    foreach (int i in mainTargets)
                        seconds = Mathf.Max(seconds, _graph.Nodes[i].Distance / speed);
                    break;
            }

            // Дополнительные миссии: грубо — крюк до региона и бой
            for (int i = 0; i < _graph.Nodes.Count; i++)
                if (!string.IsNullOrEmpty(_graph.Nodes[i].SideMissionId))
                    seconds += _graph.Nodes[i].Distance / speed + _s.fightSecondsPerObjective;

            return seconds;
        }

        /// <summary>Длина обхода целей жадно «к ближайшей» от старта.</summary>
        private float TourLength(List<int> targets, bool returnToStart)
        {
            var left = new List<int>(targets);
            int current = _graph.Start;
            float total = 0f;

            while (left.Count > 0)
            {
                var dist = _graph.Dijkstra(current, _s.tunnelWinding);
                int bestIdx = 0;
                for (int k = 1; k < left.Count; k++)
                    if (dist[left[k]] < dist[left[bestIdx]])
                        bestIdx = k;

                total += dist[left[bestIdx]];
                current = left[bestIdx];
                left.RemoveAt(bestIdx);
            }

            if (returnToStart && current != _graph.Start)
                total += _graph.Dijkstra(current, _s.tunnelWinding)[_graph.Start];

            return total;
        }

        // ---------- мелочи ----------

        private float MaxStartDistance()
        {
            float max = 0f;
            foreach (var n in _graph.Nodes)
                if (!float.IsInfinity(n.Distance) && n.Distance > max)
                    max = n.Distance;
            return max;
        }

        private float RandomRadius(LevelInfo.RegionSize size)
        {
            Vector2 range = size switch
            {
                LevelInfo.RegionSize.Small => _s.smallRadius,
                LevelInfo.RegionSize.Large => _s.largeRadius,
                _ => _s.mediumRadius
            };
            return Mathf.Lerp(range.x, range.y, (float)_rng.NextDouble());
        }

        private static float XZDist(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        private static string RoleName(LevelInfo.RegionRequirement r) =>
            r.IsSideMission ? $"{r.Role} ({r.SideMissionId})" : r.Role.ToString();
    }
}
