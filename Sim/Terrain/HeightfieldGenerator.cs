namespace CowColonySim.Sim.Terrain;

// Deterministic value-noise generator. Same seed + same field size →
// same heightfield. Multi-octave so individual verts are clearly visible
// at small scales but the whole field still has rolling shape.
public static class HeightfieldGenerator
{
    public static void Generate(Heightfield field, int seed, GenerationSettings settings)
    {
        for (var vy = 0; vy < field.VertHeight; vy++)
        {
            for (var vx = 0; vx < field.VertWidth; vx++)
            {
                var n = SampleOctaves(vx, vy, seed, settings);
                var quanta = (short)Math.Round(n * settings.AmplitudeQuanta);
                field.Set(vx, vy, quanta);
            }
        }
    }

    private static double SampleOctaves(int vx, int vy, int seed, GenerationSettings s)
    {
        var amp = 1.0;
        var freq = s.BaseFrequency;
        var sum = 0.0;
        var norm = 0.0;
        for (var oct = 0; oct < s.Octaves; oct++)
        {
            sum += amp * ValueNoise2D(vx * freq, vy * freq, seed + oct * 7919);
            norm += amp;
            amp *= s.Persistence;
            freq *= 2.0;
        }
        return sum / norm; // -1..+1
    }

    // Smooth value noise: hash lattice corners, bicubic-ish smoothstep blend.
    private static double ValueNoise2D(double x, double y, int seed)
    {
        var x0 = (int)Math.Floor(x);
        var y0 = (int)Math.Floor(y);
        var fx = x - x0;
        var fy = y - y0;
        var sx = Smoothstep(fx);
        var sy = Smoothstep(fy);

        var n00 = HashTo01(x0, y0, seed);
        var n10 = HashTo01(x0 + 1, y0, seed);
        var n01 = HashTo01(x0, y0 + 1, seed);
        var n11 = HashTo01(x0 + 1, y0 + 1, seed);

        var ix0 = Lerp(n00, n10, sx);
        var ix1 = Lerp(n01, n11, sx);
        var v = Lerp(ix0, ix1, sy);
        return v * 2.0 - 1.0;
    }

    private static double HashTo01(int x, int y, int seed)
    {
        unchecked
        {
            var h = (uint)(x * 374761393);
            h = (h ^ (uint)(y * 668265263)) * 1274126177u;
            h ^= (uint)seed * 2246822519u;
            h ^= h >> 13;
            h *= 0x85EBCA6Bu;
            h ^= h >> 16;
            return h / (double)uint.MaxValue;
        }
    }

    private static double Smoothstep(double t) => t * t * (3.0 - 2.0 * t);

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
}

public readonly record struct GenerationSettings(
    double BaseFrequency,
    int Octaves,
    double Persistence,
    int AmplitudeQuanta)
{
    public static GenerationSettings GentleHills { get; } = new(
        BaseFrequency: 0.05,
        Octaves: 4,
        Persistence: 0.5,
        AmplitudeQuanta: 12);
}
