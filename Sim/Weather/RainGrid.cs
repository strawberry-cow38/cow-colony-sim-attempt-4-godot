namespace CowColonySim.Sim.Weather;

// 2D per-tile rainfall intensity [0..1] stored as float for snapshot
// fidelity (a 0..255 byte is fine for visual but plants will eventually
// integrate small fractional contributions over many ticks). Index =
// y * Width + x.
public sealed class RainGrid
{
    public int Width { get; }
    public int Height { get; }
    public float[] Values { get; }

    public RainGrid(int width, int height)
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
