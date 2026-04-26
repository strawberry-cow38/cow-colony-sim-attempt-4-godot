namespace CowColonySim.Sim.Snapshots;

// Per-tile light values for one snapshot tick. Values is a flat
// row-major array of bytes (0..255 → 0..1). Width/Height match the
// pathfinding HeightGrid. SunFraction is the current sun contribution
// (0..1) so HUDs can show a global day/night readout without scanning
// the grid.
public sealed record LightingView(
    int Width,
    int Height,
    byte[] Values,
    float SunFraction)
{
    public static LightingView Empty { get; } = new(0, 0, Array.Empty<byte>(), 0f);

    public float Get(int tileX, int tileY)
    {
        if ((uint)tileX >= (uint)Width || (uint)tileY >= (uint)Height) return 0f;
        return Values[tileY * Width + tileX] / 255f;
    }
}
