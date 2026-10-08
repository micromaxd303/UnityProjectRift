using UnityEngine;

public class VoxelDataGenerator : IVoxelDataProvider
{
    private readonly Vector3 _worldCenter;
    private readonly Vector3 _worldMax;
    private readonly Vector3Int _chunkSize;
    private readonly float _scale; // половина наименьшего размера мира в вокселях
    private readonly Quaternion _ringInverse = Quaternion.Inverse(Quaternion.Euler(25f, 0f, 18f));

    private static readonly Vector3[] Moons =
    {
        new Vector3( 0.72f,  0.45f,  0.30f),
        new Vector3(-0.60f, -0.50f,  0.45f),
        new Vector3(-0.35f,  0.62f, -0.55f),
    };

    public VoxelDataGenerator(MarchingCubesConfig config)
    {
        _chunkSize = config.ChunkSize;
        _worldMax = Vector3.Scale(config.WorldSize, _chunkSize);
        _worldCenter = _worldMax * 0.5f;
        _scale = Mathf.Min(_worldMax.x, _worldMax.y, _worldMax.z) * 0.5f;
    }

    public float[] Generate(Vector3Int chunkCoord)
    {
        Vector3Int samples = _chunkSize + Vector3Int.one;
        int sx = samples.x, sy = samples.y, sz = samples.z;
        var data = new float[sx * sy * sz];
        Vector3 origin = Vector3.Scale(chunkCoord, _chunkSize);

        for (int z = 0; z < sz; z++)
        for (int y = 0; y < sy; y++)
        for (int x = 0; x < sx; x++)
        {
            Vector3 worldPos = origin + new Vector3(x, y, z);
            data[x + y * sx + z * sx * sy] = IsBorder(worldPos) ? -100f : SampleDensity(worldPos);
        }

        return data;
    }

    // SDF: < 0 внутри. Density: > 0 внутри (как в исходнике).
    private float SampleDensity(Vector3 worldPos)
    {
        Vector3 p = (worldPos - _worldCenter) / _scale;
        float r = p.magnitude;

        // 1. Планета с рельефом
        float planet = r - 0.55f - Fbm(p * 3f) * 0.12f;

        // 2. Гироидные пещеры, только внутри (кора остаётся целой)
        Vector3 q = p * 10f;
        float gyroid = Mathf.Sin(q.x) * Mathf.Cos(q.y)
                     + Mathf.Sin(q.y) * Mathf.Cos(q.z)
                     + Mathf.Sin(q.z) * Mathf.Cos(q.x);
        float tunnels = Mathf.Abs(gyroid) / 10f - 0.035f;
        tunnels = Mathf.Max(tunnels, r - 0.44f);
        planet = SmoothMax(planet, -tunnels, 0.02f);

        // 3. Вырезаем октант (x>0, y>0, z<0), чтобы было видно внутренности
        float octant = Mathf.Max(Mathf.Max(-p.x, -p.y), p.z);
        planet = SmoothMax(planet, -octant, 0.03f);

        // 4. Ядро
        float core = r - 0.16f - Fbm(p * 8f) * 0.03f;
        float d = Mathf.Min(planet, core);

        // 5. Наклонное кольцо
        Vector3 rp = _ringInverse * p;
        float ring = new Vector2(new Vector2(rp.x, rp.z).magnitude - 0.8f, rp.y * 2f).magnitude - 0.07f;

        // 6. Луны, плавно слитые с кольцом
        float moons = float.MaxValue;
        for (int i = 0; i < Moons.Length; i++)
            moons = Mathf.Min(moons, (p - Moons[i]).magnitude - 0.07f - Fbm(p * 12f) * 0.015f);

        d = Mathf.Min(d, SmoothMin(ring, moons, 0.08f));

        return -d * _scale;
    }

    private bool IsBorder(Vector3 worldPos)
    {
        return worldPos.x <= 0 || worldPos.x >= _worldMax.x
            || worldPos.y <= 0 || worldPos.y >= _worldMax.y
            || worldPos.z <= 0 || worldPos.z >= _worldMax.z;
    }

    // ---------- SDF-операции ----------

    private static float SmoothMin(float a, float b, float k)
    {
        float h = Mathf.Clamp01(0.5f + 0.5f * (b - a) / k);
        return Mathf.Lerp(b, a, h) - k * h * (1f - h);
    }

    private static float SmoothMax(float a, float b, float k) => -SmoothMin(-a, -b, k);

    // ---------- 3D value noise ----------

    private static float Fbm(Vector3 p)
    {
        float sum = 0f, amp = 0.5f;
        for (int i = 0; i < 4; i++)
        {
            sum += ValueNoise(p) * amp;
            p *= 2.03f;
            amp *= 0.5f;
        }
        return sum; // примерно [-1, 1]
    }

    private static float ValueNoise(Vector3 p)
    {
        int x0 = Mathf.FloorToInt(p.x), y0 = Mathf.FloorToInt(p.y), z0 = Mathf.FloorToInt(p.z);
        float fx = p.x - x0, fy = p.y - y0, fz = p.z - z0;
        float ux = fx * fx * (3f - 2f * fx);
        float uy = fy * fy * (3f - 2f * fy);
        float uz = fz * fz * (3f - 2f * fz);

        float x00 = Mathf.Lerp(Hash(x0, y0, z0),         Hash(x0 + 1, y0, z0),         ux);
        float x10 = Mathf.Lerp(Hash(x0, y0 + 1, z0),     Hash(x0 + 1, y0 + 1, z0),     ux);
        float x01 = Mathf.Lerp(Hash(x0, y0, z0 + 1),     Hash(x0 + 1, y0, z0 + 1),     ux);
        float x11 = Mathf.Lerp(Hash(x0, y0 + 1, z0 + 1), Hash(x0 + 1, y0 + 1, z0 + 1), ux);

        return Mathf.Lerp(Mathf.Lerp(x00, x10, uy), Mathf.Lerp(x01, x11, uy), uz);
    }

    private static float Hash(int x, int y, int z)
    {
        unchecked
        {
            int h = x * 374761393 + y * 668265263 + z * 1274126177;
            h = (h ^ (h >> 13)) * 1274126177;
            h ^= h >> 16;
            return (h & 0x7fffffff) / (float)int.MaxValue * 2f - 1f;
        }
    }
}