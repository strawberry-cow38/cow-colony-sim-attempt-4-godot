namespace CowColonySim.Sim.Terrain;

// Deterministic value-noise heightfield generator. fBM with configurable
// octaves, frequency, and amplitude (in quanta). No external deps; the
// hash is the same one Sim tests can pin against to get stable output.
public static class HeightfieldGenerator
{
    public readonly record struct Settings(
        int Seed = 1337,
        float BaseFrequency = 0.05f,
        int Octaves = 4,
        float Lacunarity = 2.0f,
        float Persistence = 0.5f,
        short Amplitude = 32);

    public static void Generate(Heightfield field, Settings settings)
    {
        for (var vy = 0; vy < field.VertHeight; vy++)
        {
            for (var vx = 0; vx < field.VertWidth; vx++)
            {
                var n = Fbm(vx, vy, settings);
                var h = (short)Math.Round(n * settings.Amplitude);
                field.Set(vx, vy, h);
            }
        }
    }

    private static float Fbm(int vx, int vy, Settings s)
    {
        var freq = s.BaseFrequency;
        var amp = 1f;
        var sum = 0f;
        var norm = 0f;
        for (var o = 0; o < s.Octaves; o++)
        {
            sum += amp * ValueNoise(vx * freq, vy * freq, s.Seed + o * 101);
            norm += amp;
            freq *= s.Lacunarity;
            amp *= s.Persistence;
        }
        // Output in [-1, 1].
        return (sum / norm) * 2f - 1f;
    }

    private static float ValueNoise(float x, float y, int seed)
    {
        var xi = (int)Math.Floor(x);
        var yi = (int)Math.Floor(y);
        var xf = x - xi;
        var yf = y - yi;

        var v00 = Hash01(xi, yi, seed);
        var v10 = Hash01(xi + 1, yi, seed);
        var v01 = Hash01(xi, yi + 1, seed);
        var v11 = Hash01(xi + 1, yi + 1, seed);

        var u = Smoothstep(xf);
        var v = Smoothstep(yf);

        var a = Lerp(v00, v10, u);
        var b = Lerp(v01, v11, u);
        return Lerp(a, b, v);
    }

    private static float Smoothstep(float t) => t * t * (3f - 2f * t);

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private static float Hash01(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)seed;
            h ^= (uint)x * 0x9E3779B1u;
            h = (h << 13) | (h >> 19);
            h ^= (uint)y * 0x85EBCA77u;
            h *= 0xC2B2AE3Du;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / (float)0xFFFFFF;
        }
    }
}
