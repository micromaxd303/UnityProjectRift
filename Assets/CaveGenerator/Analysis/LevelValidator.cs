using System.Collections.Generic;
using CaveGraphBuilder;
using UnityEngine;

public class LevelValidationResult
{
    public bool Success;
    public VoxelField Field;
    public LevelAnalysis Analysis;
    public SdfScene Scene;
    public PlayerRules Rules;
    public int Repairs;
    public readonly List<string> Log = new();
}

/// <summary>
/// Этапы 2–4 для одного графа: SDF-сцена → поле вокселей → анализ.
/// Непроходимые туннели расширяются и пересчитываются, пока не пройдут или не кончатся попытки.
/// Если не помогло (или проблема не в туннелях) — Success = false, нужен новый граф.
/// </summary>
public static class LevelValidator
{
    public static LevelValidationResult Run(CaveGraph graph, MarchingCubesConfig config, SdfSceneSettings sdf,
        LevelAnalysisSettings settings, MovementConfig movement, int seed)
    {
        var result = new LevelValidationResult();
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var scene = SdfSceneBuilder.Build(graph, sdf, movement, seed);
        var provider = new SdfVoxelDataProvider(scene, config);
        var field = new VoxelField(config);
        provider.FillField(field);
        result.Log.Add($"поле вокселей: {provider.ActiveChunkCount} чанков за {timer.ElapsedMilliseconds} мс");

        result.Scene = scene;
        result.Field = field;

        var rules = PlayerRules.From(settings, sdf, movement, config.VoxelSize);
        result.Rules = rules;
        result.Log.Add($"правила: прыжок {rules.JumpUp} вокс., спрыгнуть {rules.MaxFall}, " +
                       $"над головой {rules.Clearance}, уклон пола до {Mathf.Acos(rules.FloorCos) * Mathf.Rad2Deg:F0}°");

        for (int attempt = 0; ; attempt++)
        {
            timer.Restart();
            var analysis = LevelAnalyzer.Analyze(field, graph, rules, settings.requireReturnToStart);
            result.Analysis = analysis;
            result.Log.Add($"анализ #{attempt + 1}: пол {analysis.StandableCount}, достижимо {analysis.ReachableCount}, " +
                           $"проблем {analysis.Problems.Count} ({timer.ElapsedMilliseconds} мс)");
            foreach (var p in analysis.Problems)
                result.Log.Add("  " + p);

            if (analysis.Success)
            {
                var (pockets, floating) = LevelAnalyzer.Cleanup(field, analysis.SpawnIndex);
                result.Log.Add($"очистка: залито изолированных полостей {pockets} вокс., убрано висящего камня {floating} вокс.");
                int snapped = SnapAnchors(scene, field);
                result.Log.Add($"точки штампов прижаты к полу: {snapped} из {scene.Anchors.Count}");
                result.Success = true;
                return result;
            }

            if (analysis.Topological)
            {
                result.Log.Add("ремонт туннелей не поможет — нужен новый граф");
                return result;
            }

            if (attempt >= settings.maxRepairAttempts)
            {
                result.Log.Add($"туннели не починились за {settings.maxRepairAttempts} попыт.");
                return result;
            }

            // Ремонт: расширяем непроходимые туннели и пересчитываем только задетые чанки
            var regions = new HashSet<ushort>();
            foreach (int ei in analysis.FailedEdges)
                regions.Add((ushort)(graph.Nodes.Count + ei + 1));

            var chunks = provider.InflateRegions(regions, settings.repairInflate);
            foreach (var coord in chunks)
                provider.FillChunk(coord, field);

            result.Repairs++;
            result.Log.Add($"ремонт #{result.Repairs}: расширено туннелей {regions.Count}, пересчитано чанков {chunks.Count}");
        }
    }

    /// <summary>
    /// Точки ниш и пятачков ставятся на настоящую поверхность пола в поле вокселей:
    /// шум сдвигает пол на десятки сантиметров, и без этого инкубатор висит или тонет.
    /// Укрытия и колонны не трогаем — их точка внутри камня.
    /// </summary>
    private static int SnapAnchors(SdfScene scene, VoxelField field)
    {
        int snapped = 0;
        for (int i = 0; i < scene.Anchors.Count; i++)
        {
            var a = scene.Anchors[i];
            if (a.Kind is not (SdfAnchorKind.Incubator or SdfAnchorKind.Pad)) continue;

            Vector3Int v = field.FromLocal(a.Position);
            int top = Mathf.Min(field.Size.y - 1, v.y + 3);
            int bottom = Mathf.Max(1, v.y - 4);

            for (int y = top; y >= bottom; y--)
            {
                int air = field.Index(v.x, y, v.z);
                int below = field.Index(v.x, y - 1, v.z);
                if (!field.IsAir(air) || field.IsAir(below)) continue;

                // Поверхность между сэмплами: где плотность проходит через уровень изоповерхности
                float d0 = field.Density[below] - field.IsoLevel;
                float d1 = field.Density[air] - field.IsoLevel;
                float t = Mathf.Clamp01(d0 / (d0 - d1));
                a.Position.y = field.ToLocal(v.x, y - 1, v.z).y + t * field.VoxelSize;
                scene.Anchors[i] = a;
                snapped++;
                break;
            }
        }
        return snapped;
    }
}
