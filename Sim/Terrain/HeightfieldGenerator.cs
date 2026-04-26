namespace CowColonySim.Sim.Terrain;

// Deterministic value-noise heightfield generator. fBM with configurable
// octaves, frequency, and amplitude (in quanta). No external deps; the
// hash is the same one Sim tests can pin against to get stable output.
public static class HeightfieldGenerator
{
    // BaseFrequency 0.05 → fundamental wavelength ≈ 20 tiles, so a 256-tile
    // world gets ~12 hill peaks in each direction (visibly hilly, not "one
    // giant slope"). Amplitude 60 quanta = 45 m, plenty above colonist
    // scale. SmoothingPasses 3 keeps the silhouettes round.
    public readonly record struct Settings(
        int Seed = 1337,
        float BaseFrequency = 0.05f,
        int Octaves = 4,
        float Lacunarity = 2.0f,
        float Persistence = 0.5f,
        short Amplitude = 60,
        float OriginTilesX = 0f,
        float OriginTilesY = 0f,
        float TileSpacing = 1f,
        int SmoothingPasses = 3);

    public static void Generate(Heightfield field, Settings settings)
    {
        for (var vy = 0; vy < field.VertHeight; vy++)
        {
            for (var vx = 0; vx < field.VertWidth; vx++)
            {
                var sx = settings.OriginTilesX + vx * settings.TileSpacing;
                var sy = settings.OriginTilesY + vy * settings.TileSpacing;
                var n = Fbm(sx, sy, settings);
                var h = (short)Math.Round(n * settings.Amplitude);
                field.Set(vx, vy, h);
            }
        }
        for (var pass = 0; pass < settings.SmoothingPasses; pass++)
        {
            BoxBlur(field);
        }
        field.MarkChanged();
    }

    // 3×3 box blur over the vertex grid. Edges clamp to in-bounds samples.
    // Faceted rendering is unaffected — the per-tile 4-corner copy lives in
    // the mesh builder, not the source data — so smoothing the source just
    // softens the hill shapes the renderer faces off of.
    private static void BoxBlur(Heightfield field)
    {
        var w = field.VertWidth;
        var h = field.VertHeight;
        var src = field.AsReadOnlySpan().ToArray();
        for (var vy = 0; vy < h; vy++)
        {
            for (var vx = 0; vx < w; vx++)
            {
                var sum = 0;
                var count = 0;
                for (var oy = -1; oy <= 1; oy++)
                {
                    var sy = vy + oy;
                    if ((uint)sy >= (uint)h) continue;
                    for (var ox = -1; ox <= 1; ox++)
                    {
                        var sx = vx + ox;
                        if ((uint)sx >= (uint)w) continue;
                        sum += src[sy * w + sx];
                        count++;
                    }
                }
                field.Set(vx, vy, (short)(sum / count));
            }
        }
    }

    private static float Fbm(float vx, float vy, Settings s)
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
