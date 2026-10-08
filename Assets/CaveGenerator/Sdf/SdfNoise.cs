using UnityEngine;

/// <summary>3D value noise и fBm для SDF-сцены (те же функции, что в VoxelDataGenerator).</summary>
public static class SdfNoise
{
    public static float Fbm(Vector3 p)
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

    public static float ValueNoise(Vector3 p)
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

    public static float SmoothMin(float a, float b, float k)
    {
        float h = Mathf.Clamp01(0.5f + 0.5f * (b - a) / k);
        return Mathf.Lerp(b, a, h) - k * h * (1f - h);
    }
}
