using CowColonySim.Sim.Map;
using CowColonySim.Sim.Systems;
using CowColonySim.Sim.Time;

namespace CowColonySim.Sim.Climate;

public sealed class ClimateTickSystem : ITickSystem
{
    private readonly MapSettings _settings;
    private readonly ClimateState _state;

    public string Name => "climate";

    public ClimateTickSystem(MapSettings settings, ClimateState state)
    {
        _settings = settings;
        _state = state;
    }

    public void Tick(in TickContext ctx)
    {
        var gameTime = GameClock.ToDateTime(ctx.TickNumber);
        var dayFraction = GameClock.DayFraction(ctx.TickNumber);
        var globalC = TemperatureModel.GlobalSurfaceC(gameTime, dayFraction, _settings.Latitude);
        var season = SeasonHelper.FromDate(gameTime, _settings.Latitude);
        var degrees = WindModel.DirectionDegrees(_settings.Seed, ctx.TickNumber);
        var speed = WindModel.SpeedMetresPerSecond(_settings.Seed, ctx.TickNumber);

        _state.Publish(new ClimateSnapshot(
            GlobalSurfaceC: globalC,
            Season: season,
            Biome: _settings.Biome,
            WindDegrees: degrees,
            WindSpeedMps: speed,
            WindDirection: CompassHelper.FromDegrees(degrees),
            WindCategory: WindCategoryHelper.FromSpeed(speed)));
    }
}
