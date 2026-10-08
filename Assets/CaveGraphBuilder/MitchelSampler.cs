using System.Collections.Generic;
using UnityEngine;

public class MitchelSampler : MonoBehaviour
{
    public MarchingCubesConfig _config;
    public Vector2 _borderDelta;

    public int desiredNodesCount = 10;
    public int sampleCount = 10;
    public int sampleGrowth = 1;

    public int seed = 1;

    [Header("Height")]
    public float heightAmplitude = 20f;
    public float noiseScale = 0.1f;

    private float _offsetX, _offsetY;
    private List<Vector3> _nodes = new();

    public List<Vector3> GenerateNode()
    {
        // свой генератор: не трогаем глобальный UnityEngine.Random
        var rng = new System.Random(seed);

        _offsetX = rng.Next(-10000, 10000);
        _offsetY = rng.Next(-10000, 10000);

        _borderDelta = new Vector2(
            _config.WorldSize.x * _config.ChunkSize.x,
            _config.WorldSize.z * _config.ChunkSize.z) * _config.VoxelSize / 2 - new Vector2(15f, 15f);

        var points = new List<Vector2>(desiredNodesCount);

        while (points.Count < desiredNodesCount)
        {
            int candidates = sampleCount + sampleGrowth * points.Count;

            Vector2 best = default;
            float bestDistSqr = -1f;

            for (int i = 0; i < candidates; i++)
            {
                var sample = new Vector2(
                    NextRange(rng, -_borderDelta.x, _borderDelta.x),
                    NextRange(rng, -_borderDelta.y, _borderDelta.y));

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
                GetNoise(points[i].x, points[i].y) * heightAmplitude,
                points[i].y));
        }

        return result;
    }

    public float GetNoise(float x, float y)
    {
        float n = Mathf.PerlinNoise(
            (x + _offsetX) * noiseScale,
            (y + _offsetY) * noiseScale);

        return n * 2f - 1f; // 0..1 -> -1..1
    }

    private static float NextRange(System.Random rng, float min, float max)
    {
        return min + (float)rng.NextDouble() * (max - min);
    }

    [ContextMenu("Generate")]
    public void Generate()
    {
        _nodes = GenerateNode();
    }

    private void OnDrawGizmosSelected()
    {
        if (_config == null) return;

        // рамка области генерации в локальных координатах трансформа
        Vector2 border = new Vector2(
            _config.WorldSize.x * _config.ChunkSize.x,
            _config.WorldSize.z * _config.ChunkSize.z) * _config.VoxelSize / 2 - new Vector2(15f, 15f);

        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = Color.magenta;
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(border.x * 2f, heightAmplitude * 2f, border.y * 2f));
        Gizmos.matrix = Matrix4x4.identity;

        if (_nodes == null) return;

        for (int i = 0; i < _nodes.Count; i++)
        {
            Vector3 world = transform.TransformPoint(_nodes[i]);

            // градиент по порядку добавления: красный -> зелёный
            float t = _nodes.Count > 1 ? i / (float)(_nodes.Count - 1) : 0f;
            Gizmos.color = Color.Lerp(Color.red, Color.green, t);
            Gizmos.DrawSphere(world, 1f);

#if UNITY_EDITOR
            UnityEditor.Handles.Label(world + Vector3.up * 1.5f, i.ToString());
#endif
        }
    }
}