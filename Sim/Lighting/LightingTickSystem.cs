using CowColonySim.Sim.Map;
using CowColonySim.Sim.Systems;
using CowColonySim.Sim.Time;

namespace CowColonySim.Sim.Lighting;

public sealed class LightingTickSystem : ITickSystem
{
    private readonly DayLightWindow _window;
    private readonly TileLightingApi _api;
    private readonly SkyExposureSystem _exposure;
    private readonly ArtificialLightSystem _artificial;

    public string Name => "lighting";

    public LightingTickSystem(
        MapSettings settings,
        TileLightingApi api,
        SkyExposureSystem exposure,
        ArtificialLightSystem artificial)
    {
        _window = settings.EffectiveDayLight;
        _api = api;
        _exposure = exposure;
        _artificial = artificial;
    }

    public void Tick(in TickContext ctx)
    {
        var dayFraction = GameClock.DayFraction(ctx.TickNumber);
        var sunByte = SunModel.ComputeSunByte(dayFraction, _window);
        _api.SetGlobalSun(sunByte);
        _exposure.RebuildDirty();
        _artificial.RebuildIfDirty();
    }
}
