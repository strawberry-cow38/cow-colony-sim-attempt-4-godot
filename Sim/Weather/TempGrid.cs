namespace CowColonySim.Sim.Weather;

// 2D per-tile temperature in Celsius. Float (not byte) because we need
// negatives for cold climates and precision for plant growth gating.
// Surface-only — z is implicit. Index = y * Width + x.
public sealed class TempGrid
{
    public int Width { get; }
    public int Height { get; }
    public float[] Values { get; }

    public TempGrid(int width, int height)
    {
        Width = width;
        Height = height;
        Values = new float[width * height];
    }

    public float Get(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return 0f;
        return Values[y * Width + x];
    }

    public void Fill(float value) => Array.Fill(Values, value);

    public float[] Clone() => (float[])Values.Clone();
}
