using System.Collections.Generic;
using UnityEngine;

namespace CaveGraphBuilder
{
    public class MitchelSampler
    {
        private MitchelSamplerSettings settings;
        
        private int seed;
        private float _offsetX, _offsetY;
        
        /// <summary>
        /// Результат класса
        /// </summary>
        private List<Vector3> nodes = new();

        public MitchelSampler(MitchelSamplerSettings settings)
        {
            this.settings = settings;
            seed = 1;
        }
        public MitchelSampler(MitchelSamplerSettings settings, int seed) : this(settings)
        {
            this.seed = seed;
        }
        public List<Vector3> GenerateNode(Vector2 borderDelta)
        {
            var rng = new System.Random(seed);

            _offsetX = rng.Next(-10000, 10000);
            _offsetY = rng.Next(-10000, 10000);

            var points = new List<Vector2>(settings.desiredNodesCount);

            while (points.Count < settings.desiredNodesCount)
            {
                int candidates = settings.sampleCount + settings.sampleGrowth * points.Count;

                Vector2 best = default;
                float bestDistSqr = -1f;

                for (int i = 0; i < candidates; i++)
                {
                    var sample = new Vector2(
                        NextRange(rng, -borderDelta.x, borderDelta.x),
                        NextRange(rng, -borderDelta.y, borderDelta.y));

                    // расстояние до ближайшей существующей точки
                    float nearestSqr = float.MaxValue;
                    for (int j = 0; j < points.Count; j++)
                    {
                        float d = (points[j] - sample).sqrMagnitude;
                        if (d < nearestSqr) nearestSqr = d;
                    }

                    // выбираем кандидата с самым большим "ближайшим" расстоянием
                    if (nearestSqr > bestDistSqr)
                    {
                        bestDistSqr = nearestSqr;
                        best = sample;
                    }
                }

                points.Add(best);
            }

            var result = new List<Vector3>(points.Count);

            for (int i = 0; i < points.Count; i++)
            {
                // Vector2.y -> ось Z
                result.Add(new Vector3(
                    points[i].x,
                    GetNoise(points[i].x, points[i].y) * settings.heightAmplitude,
                    points[i].y));
            }

            return result;
        }
        public float GetNoise(float x, float y)
        {
            float n = Mathf.PerlinNoise(
                (x + _offsetX) * settings.noiseScale,
                (y + _offsetY) * settings.noiseScale);

            return n * 2f - 1f; // 0..1 -> -1..1
        }
        private float NextRange(System.Random rng, float min, float max)
        {
            return min + (float)rng.NextDouble() * (max - min);
        }
    }
}