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
        Temperature.Fill(CurrentCelsius);
        Rainfall.Fill(CurrentRainfall);
    }
}
