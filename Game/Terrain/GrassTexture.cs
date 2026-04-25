using Godot;

namespace CowColonySim.Game.Terrain;

// Smooth low-frequency value-noise grass: a green base modulated by a soft
// noise field, plus a tiny per-pixel jitter. Produces an organic grassy
// look without the harsh dark speckle caused by per-pixel outliers.
public static class GrassTexture
{
    public static ImageTexture Build(int seed = 1, int size = 128, int cells = 12)
    {
        var rng = new System.Random(seed);

        var grid = new float[(cells + 1) * (cells + 1)];
        for (var i = 0; i < grid.Length; i++) grid[i] = (float)rng.NextDouble();

        var img = Image.CreateEmpty(size, size, false, Image.Format.Rgb8);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var fx = x * cells / (float)size;
                var fy = y * cells / (float)size;
                var gx = (int)fx;
                var gy = (int)fy;
                var tx = SmoothStep(fx - gx);
                var ty = SmoothStep(fy - gy);
                var a = grid[gy * (cells + 1) + gx];
                var b = grid[gy * (cells + 1) + gx + 1];
                var c = grid[(gy + 1) * (cells + 1) + gx];
                var d = grid[(gy + 1) * (cells + 1) + gx + 1];
                var ab = Mathf.Lerp(a, b, tx);
                var cd = Mathf.Lerp(c, d, tx);
                var noise = Mathf.Lerp(ab, cd, ty);

                var jitter = ((float)rng.NextDouble() - 0.5f) * 0.06f;
                var t = noise + jitter;

                var r = 0.20f + 0.10f * t;
                var g = 0.44f + 0.18f * t;
                var bl = 0.18f + 0.08f * t;
                img.SetPixel(x, y, new Color(r, g, bl));
            }
        }
        return ImageTexture.CreateFromImage(img);
    }

    private static float SmoothStep(float t) => t * t * (3f - 2f * t);
}
