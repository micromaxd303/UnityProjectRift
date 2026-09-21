using System.Collections.Generic;
using UnityEngine;

public static class MitchelSampler
{
    public static List<GraphNode> Generate(
        MitchelConfig config, 
        Vector3 worldSize,
        Vector3 worldCenter,
        int seed = 0
        )
    {
        var random = seed == 0
            ? new System.Random()
            : new System.Random(seed);
        
        var result = new List<GraphNode>();
        var positionsXZ = new List<Vector2>();
        
        var anchors = config.anchorNodes;
        
        foreach (var anchor in anchors)
        {
            result.Add(anchor);
            positionsXZ.Add(new Vector2(anchor.Position.x, anchor.Position.z));
        }
        

        for (int i = 0; i < config.nodeCount; i++)
        {
            int nodeCount = positionsXZ.Count - anchors.Count;
            int m = config.candidatesPerPoint + config.growPerPoint * nodeCount;

            Vector2 best = Vector2.zero;
            float bestDistance = -1f;
            int batches = 0;

            do
            {
                for (int j = 0; j < m; j++)
                {
                    Vector2 current = RandomInRegion(random, worldSize, worldCenter);
                    float d2 = NearestSquareDistanceXZ(current, positionsXZ);
                    if (d2 > bestDistance)
                    {
                        bestDistance = d2;
                        best = current;
                    }
                }

                batches++;
            } while (bestDistance < config.rMin * config.rMin && batches <= config.maxExtraBatches);

#if UNITY_EDITOR
            if (bestDistance < config.rMin * config.rMin)
            {
                Debug.LogWarning("Слишком маленький регион");
            }
#endif

            AddGenerated(best, result, positionsXZ);
        }

        return result;
    }

    private static Vector2 RandomInRegion(System.Random rnd, Vector3 worldSize, Vector3 worldCenter)
    {
        float x = worldCenter.x + ((float)rnd.NextDouble() - 0.5f) * worldSize.x;
        float z = worldCenter.z + ((float)rnd.NextDouble() - 0.5f) * worldSize.z;
        return new Vector2(x, z);
    }

    private static float NearestSquareDistanceXZ(Vector2 current, List<Vector2> placed)
    {
        float min = float.PositiveInfinity;
        for (int i = 0; i < placed.Count; i++)
        {
            float dx = current.x - placed[i].x;
            float dz = current.y - placed[i].y;
            float d2 = dx * dx + dz * dz;
            if (d2 < min) min = d2;
        }
        return min;
    }

    private static void AddGenerated(Vector2 xz, List<GraphNode> result, List<Vector2> placedXZ)
    {
        float y = DefaultHeightFunc(xz.x, xz.y); // хардкод
        result.Add(new GraphNode(new Vector3(xz.x, y, xz.y), NodeRole.Empty, false));
        placedXZ.Add(xz);
    }

    private static float DefaultHeightFunc(float x, float z)
    {
        float n = Mathf.PerlinNoise(x * 0.05f, z * 0.05f);
        return (n - 0.5f) * 2f * 5f; // хардкод
    }
}