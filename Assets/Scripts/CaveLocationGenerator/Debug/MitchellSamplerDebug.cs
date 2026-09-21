using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rift.Generation
{
    // Роль узла в графе локации. Якоря (вход/цель) задаёшь заранее,
    // остальные узлы расставляет Mitchell. Роль нужна следующему этапу —
    // построению графа (вход и цель держим на костяке, доп. миссии делаем тупиками).
    public enum NodeRole
    {
        Entrance,    // вход на уровень
        Objective,   // главная цель / выход к боссу
        Combat,      // обычная боевая арена (генерируется)
        SideMission  // комната доп. миссии (роль навешиваешь позже, на этапе графа)
    }

    public struct LevelNode
    {
        public Vector3 Position;
        public NodeRole Role;
        public bool IsAnchor;   // true — узел задан вручную, не сгенерирован

        public LevelNode(Vector3 position, NodeRole role, bool isAnchor)
        {
            Position = position;
            Role = role;
            IsAnchor = isAnchor;
        }
    }

    // Все настройки расстановки. Регион задаётся в плоскости XZ — это вид
    // сверху на пол локации. Высота (Y) назначается отдельно после расстановки.
    [Serializable]
    public class MitchellSettings
    {
        [Header("Количество")]
        public int nodeCount = 8;                          // итоговое N, ВКЛЮЧАЯ якоря

        [Header("Регион расстановки (XZ). y компоненты = ось Z")]
        public Vector2 regionCenter = Vector2.zero;        // центр пола: (x, z)
        public Vector2 regionSize = new Vector2(40f, 40f); // размеры: (по X, по Z)

        [Header("Дистанции")]
        public float rMin = 6f;                            // жёсткий пол: ближе арены не встанут

        [Header("Качество выборки (кандидаты Mitchell)")]
        public int candidatesPerPoint = 20;                // базовое m
        public int growthPerPoint = 1;                     // m растёт: m = base + growth * (уже_поставлено)
        public int maxExtraBatches = 6;                    // доп. партий кандидатов, если не лезем в rMin

        [Header("Высота (Y) — когерентный шум вместо чистого рандома")]
        public float heightAmplitude = 3f;                 // полуразмах высоты [-amp, +amp]
        public float noiseScale = 0.05f;                   // масштаб шума: меньше = плавнее склоны
        public Vector2 noiseOffset = Vector2.zero;         // сдвиг шума (можно завязать на seed для вариативности)
    }

    public static class MitchellSampler
    {
        // Главный вход. anchors — заранее размеченные узлы (вход, цель); могут быть null/пустыми.
        // heightFunc — необязательная своя функция высоты (x, z) -> y; если null, берётся когерентный Perlin.
        public static List<LevelNode> Generate(
            MitchellSettings s,
            IList<LevelNode> anchors = null,
            int seed = 0,
            Func<float, float, float> heightFunc = null)
        {
            var rng = new System.Random(seed);
            heightFunc ??= (x, z) => DefaultHeight(x, z, s);

            var result = new List<LevelNode>();
            // Параллельный список XZ-позиций всех уже стоящих точек — только для проверки дистанций.
            var placedXZ = new List<Vector2>();

            // 1) Якоря идут первыми и работают как стартовые «семена» Mitchell.
            if (anchors != null)
            {
                foreach (var a in anchors)
                {
                    result.Add(a);
                    placedXZ.Add(new Vector2(a.Position.x, a.Position.z));
                }
            }

            int toGenerate = Mathf.Max(0, s.nodeCount - result.Count);

            // 2) Если якорей нет вообще — первой точке не от кого убегать, ставим случайно.
            if (placedXZ.Count == 0 && toGenerate > 0)
            {
                Vector2 first = RandomInRegion(rng, s);
                AddGenerated(first, s, heightFunc, result, placedXZ);
                toGenerate--;
            }

            float rMinSq = s.rMin * s.rMin;

            // 3) Основной цикл: ровно toGenerate раз ставим по одному «лучшему» узлу.
            for (int i = 0; i < toGenerate; i++)
            {
                int placedCount = placedXZ.Count;
                int m = s.candidatesPerPoint + s.growthPerPoint * placedCount;

                Vector2 best = Vector2.zero;
                float bestSqDist = -1f;     // копим лучшего кандидата ПОПЕРЁК всех партий
                int batches = 0;

                // Генерируем партиями: если лучший кандидат не дотянул до rMin —
                // пространство почти насыщено, пробуем ещё кандидатов, но с потолком.
                do
                {
                    for (int j = 0; j < m; j++)
                    {
                        Vector2 c = RandomInRegion(rng, s);
                        float d2 = NearestSqDistXZ(c, placedXZ); // до ближайшей уже стоящей точки
                        if (d2 > bestSqDist)
                        {
                            bestSqDist = d2;
                            best = c;
                        }
                    }
                    batches++;
                }
                while (bestSqDist < rMinSq && batches <= s.maxExtraBatches);

                // Если даже лучший кандидат ближе rMin — регион перенасыщен для такого rMin.
                // Ставим best-effort (иначе застрянем), но сигналим: пора уменьшить N или rMin,
                // либо увеличить регион (см. RegionSizeForSpacing).
                if (bestSqDist < rMinSq)
                {
                    Debug.LogWarning(
                        $"[MitchellSampler] Узел {placedCount} не уложился в rMin={s.rMin}. " +
                        $"Регион перенасыщен: уменьши N/rMin или увеличь regionSize.");
                }

                AddGenerated(best, s, heightFunc, result, placedXZ);
            }

            return result;
        }

        // Удобный помощник: размер квадратного региона под желаемый спейсинг и N.
        // Выводится из (площадь / N)^(1/2) ≈ spacing  =>  сторона = spacing * sqrt(N).
        // Так ты задаёшь длину коридоров, а не подбираешь регион вслепую.
        public static Vector2 RegionSizeForSpacing(float spacing, int nodeCount)
        {
            float side = spacing * Mathf.Sqrt(Mathf.Max(1, nodeCount));
            return new Vector2(side, side);
        }

        // --- внутреннее ---

        private static void AddGenerated(
            Vector2 xz, MitchellSettings s, Func<float, float, float> heightFunc,
            List<LevelNode> result, List<Vector2> placedXZ)
        {
            float y = heightFunc(xz.x, xz.y); // xz.y здесь — это координата Z
            result.Add(new LevelNode(new Vector3(xz.x, y, xz.y), NodeRole.Combat, false));
            placedXZ.Add(xz);
        }

        private static Vector2 RandomInRegion(System.Random rng, MitchellSettings s)
        {
            float x = s.regionCenter.x + ((float)rng.NextDouble() - 0.5f) * s.regionSize.x;
            float z = s.regionCenter.y + ((float)rng.NextDouble() - 0.5f) * s.regionSize.y;
            return new Vector2(x, z);
        }

        // Квадрат расстояния до ближайшей точки. Сравниваем квадраты — без sqrt.
        // Брутфорс O(n): для десятков узлов это микросекунды. Понадобятся сотни/тысячи —
        // подменишь этот метод на запрос к фоновой сетке, сам алгоритм не изменится.
        private static float NearestSqDistXZ(Vector2 c, List<Vector2> placed)
        {
            float min = float.PositiveInfinity;
            for (int i = 0; i < placed.Count; i++)
            {
                float dx = c.x - placed[i].x;
                float dz = c.y - placed[i].y;
                float d2 = dx * dx + dz * dz;
                if (d2 < min) min = d2;
            }
            return min; // при пустом списке вернётся +inf — любой кандидат подойдёт
        }

        // Когерентная высота: соседние комнаты связаны плавным перепадом ->
        // осмысленные склоны под подкат/slide boost, а не хаотичные ступеньки.
        private static float DefaultHeight(float x, float z, MitchellSettings s)
        {
            float n = Mathf.PerlinNoise(
                x * s.noiseScale + s.noiseOffset.x,
                z * s.noiseScale + s.noiseOffset.y);     // [0, 1]
            return (n - 0.5f) * 2f * s.heightAmplitude;  // [-amp, +amp]
        }
    }

    // Необязательный компонент для отладки: вешаешь на пустой GameObject,
    // двигаешь параметры в инспекторе и сразу видишь расстановку через гизмо.
    public class MitchellSamplerDebug : MonoBehaviour
    {
        public MitchellSettings settings = new MitchellSettings();
        public int seed = 0;
        [Tooltip("Авто-размер региона из spacing и N (перебивает regionSize).")]
        public bool autoRegionFromSpacing = false;
        public float targetSpacing = 8f;

        public Vector3 entrance = new Vector3(-15f, 0f, -15f);
        public Vector3 objective = new Vector3(15f, 0f, 15f);

        private List<LevelNode> _nodes;

        private void OnValidate()
        {
            if (autoRegionFromSpacing)
                settings.regionSize = MitchellSampler.RegionSizeForSpacing(targetSpacing, settings.nodeCount);
            Rebuild();
        }

        [ContextMenu("Rebuild")]
        public void Rebuild()
        {
            var anchors = new List<LevelNode>
            {
                new LevelNode(entrance,  NodeRole.Entrance,  true),
                new LevelNode(objective, NodeRole.Objective, true),
            };
            _nodes = MitchellSampler.Generate(settings, anchors, seed);
        }

        private void OnDrawGizmos()
        {
            if (_nodes == null) Rebuild();

            // Контур региона расстановки (XZ).
            Gizmos.color = Color.gray;
            Vector3 center = new Vector3(settings.regionCenter.x, 0f, settings.regionCenter.y);
            Gizmos.DrawWireCube(center, new Vector3(settings.regionSize.x, 0.1f, settings.regionSize.y));

            foreach (var n in _nodes)
            {
                Gizmos.color = n.Role switch
                {
                    NodeRole.Entrance  => Color.green,
                    NodeRole.Objective => Color.red,
                    _                  => Color.cyan,
                };
                Gizmos.DrawSphere(n.Position, 0.6f);

                // Полупрозрачная сфера rMin — видно, что арены не пересекаются.
                Gizmos.color = new Color(1f, 1f, 1f, 0.06f);
                Gizmos.DrawSphere(n.Position, settings.rMin * 0.5f);
            }
        }
    }
}