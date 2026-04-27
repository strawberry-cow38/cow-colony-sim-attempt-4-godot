using CowColonySim.Sim.Weather;
using CowColonySim.Sim.World;

namespace CowColonySim.Sim.Systems;

// Drives the per-tile temperature and rainfall grids each tick. Until
// per-cell climate / biome inputs land, both grids are filled with a
// single global value pulled from the time-of-day curve. The grid
// shape is in place so future systems (heating units, terrain wind
// shadows, biome maps) can paint per-cell deltas.
public sealed class WeatherSystem : ITickSystem
{
    private readonly SimWorld _world;
    public TempGrid Temperature { get; }
    public RainGrid Rainfall { get; }
    public float CurrentCelsius { get; private set; }
    public float CurrentRainfall { get; private set; }

    public WeatherSystem(SimWorld world, int width, int height)
    {
        _world = world;
        Temperature = new TempGrid(width, height);
        Rainfall = new RainGrid(width, height);
    }

    public void Tick(TickContext ctx)
    {
        CurrentCelsius = TempCurve.CelsiusAtTick(ctx.TickNumber);
        CurrentRainfall = RainCurve.IntensityAtTick(ctx.TickNumber);
        Temperature.Fill(CurrentCelsius);
        Rainfall.Fill(CurrentRainfall);
    }
}
