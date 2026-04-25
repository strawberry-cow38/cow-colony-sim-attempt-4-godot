using Godot;

namespace CowColonySim.Game.Terrain;

// Procedural per-pixel grass texture: noisy mid-green with occasional darker
// patches to break up the surface. Generated in C# (no shader needed) and
// packaged as a small ImageTexture sampled once per tile.
public static class GrassTexture
{
    public static ImageTexture Build(int seed = 1, int size = 64)
    {
        var img = Image.CreateEmpty(size, size, false, Image.Format.Rgb8);
        var rng = new System.Random(seed);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var t = (float)rng.NextDouble();
                var r = 0.18f + 0.10f * t;
                var g = 0.42f + 0.20f * t;
                var b = 0.16f + 0.08f * t;
                if (rng.NextDouble() < 0.06)
                {
                    r *= 0.55f;
                    g *= 0.55f;
                    b *= 0.55f;
                }
                img.SetPixel(x, y, new Color(r, g, b));
            }
        }
        return ImageTexture.CreateFromImage(img);
    }
}
