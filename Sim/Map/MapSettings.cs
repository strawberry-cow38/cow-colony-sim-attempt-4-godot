namespace CowColonySim.Sim.Map;

public sealed record MapSettings(
    int Width = 256,
    int Height = 256,
    int MinZ = -64,
    int MaxZ = 64,
    double Latitude = 45.0,
    double Longitude = 0.0,
    int Seed = 0,
    DayLightWindow DayLight = default)
{
    public int Depth => MaxZ - MinZ;
    public int TileCount => Width * Height * Depth;
    public DayLightWindow EffectiveDayLight =>
        DayLight.Equals(default(DayLightWindow)) ? DayLightWindow.Default : DayLight;
}

public readonly record struct DayLightWindow(
    double DawnStart,
    double DawnEnd,
    double DuskStart,
    double DuskEnd)
{
    public static DayLightWindow Default { get; } = new(
        DawnStart: 5.5 / 24.0,
        DawnEnd: 6.5 / 24.0,
        DuskStart: 17.5 / 24.0,
        DuskEnd: 18.5 / 24.0);
}
