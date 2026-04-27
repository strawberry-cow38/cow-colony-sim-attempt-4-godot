namespace CowColonySim.Sim.Snapshots;

// Per-tile temperature (Celsius) and rainfall intensity (0..1) for one
// tick. Both are flat row-major float arrays sized Width*Height. Current
// values are also surfaced as scalars so HUDs can show a global readout
// without scanning the grid.
public sealed record WeatherView(
    int Width,
    int Height,
    float[] Temperature,
    float[] Rainfall,
    float CurrentCelsius,
    float CurrentRainfall)
{
    public static WeatherView Empty { get; } = new(0, 0, Array.Empty<float>(), Array.Empty<float>(), 0f, 0f);

    public float TempAt(int tileX, int tileY)
    {
        if ((uint)tileX >= (uint)Width || (uint)tileY >= (uint)Height) return 0f;
        return Temperature[tileY * Width + tileX];
    }

    public float RainAt(int tileX, int tileY)
    {
        if ((uint)tileX >= (uint)Width || (uint)tileY >= (uint)Height) return 0f;
        return Rainfall[tileY * Width + tileX];
    }
}
