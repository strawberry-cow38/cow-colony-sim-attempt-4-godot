using CowColonySim.Sim.Weather;
using CowColonySim.Sim.World;

namespace CowColonySim.Sim.Systems;

// Drives the per-tile temperature and rainfall grids each tick. Until
// per-cell climate / biome inputs land, both grids are filled with a
// single global value pulled from the map climate + time-of-year +
// time-of-day curves. The grid shape is in place so future systems
// (heating units, terrain wind shadows, biome maps) can paint per-cell
// deltas.
public sealed class WeatherSystem : ITickSystem
{
    private readonly SimWorld _world;
    public TempGrid Temperature { get; }
    public RainGrid Rainfall { get; }
    public MapClimate Climate { get; }
    public float CurrentCelsius { get; private set; }
    public float CurrentRainfall { get; private set; }
    // World-space yaw the wind blows TOWARD, in radians. 0 = +Z. Cycles
    // slowly with tick so the gimbal arrow drifts instead of jittering.
    public float CurrentWindRad { get; private set; }
    // Wind speed, m/s. Same cheap synthesized curve until biome inputs land.
    public float CurrentWindSpeed { get; private set; }

    public WeatherSystem(SimWorld world, int width, int height, MapClimate climate)
    {
        _world = world;
        Temperature = new TempGrid(width, height);
        Rainfall = new RainGrid(width, height);
        Climate = climate;
    }

    public void Tick(TickContext ctx)
    {
        CurrentCelsius = TempCurve.CelsiusAtTick(ctx.TickNumber, Climate);
        CurrentRainfall = RainCurve.IntensityAtTick(ctx.TickNumber, Climate);
        // Cheap continuous wind: slow primary rotation + low-amplitude
        // secondary so direction wanders naturally over a sim day.
        var t = ctx.TickNumber * (1f / 3600f); // ≈ 1 rad / minute base
        CurrentWindRad = MathF.Atan2(
            MathF.Sin(t) + 0.3f * MathF.Sin(t * 4.7f),
            MathF.Cos(t) + 0.3f * MathF.Cos(t * 3.1f));
        // 2..14 m/s baseline + storm boost when rainfall climbs.
        var gust = 0.5f + 0.5f * MathF.Sin(t * 1.7f);
        CurrentWindSpeed = 2f + 8f * gust + 6f * CurrentRainfall;
        Temperature.Fill(CurrentCelsius);
        Rainfall.Fill(CurrentRainfall);
    }
}
