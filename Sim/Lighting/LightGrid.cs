namespace CowColonySim.Sim.Lighting;

// 2D per-tile light values [0..1] quantised to bytes for cheap storage
// and snapshot copy. Surface-only — z is implicit (the walkable top of
// each (x, y) column). Index = y * Width + x.
public sealed class LightGrid
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Values { get; }

    public LightGrid(int width, int height)
    {
        Width = width;
        Height = height;
        Values = new byte[width * height];
    }

    public float Get(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return 0f;
        return Values[y * Width + x] / 255f;
    }

    public void Fill(byte value) => Array.Fill(Values, value);

    public void ApplyMax(int x, int y, byte value)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return;
        var i = y * Width + x;
        if (value > Values[i]) Values[i] = value;
    }

    public byte[] Clone() => (byte[])Values.Clone();
}
