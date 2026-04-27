using CowColonySim.Sim.Time;

namespace CowColonySim.Sim.Weather;

// Maps tick → rainfall intensity [0..1]. Simple multi-day cosine cycle
// with most of the time at zero so the world isn't permanently soggy.
// A real weather system will replace this with stochastic fronts.
public static class RainCurve
{
    private const double CyclePeriodSeconds = 86400.0 * 3.0; // dry/wet over ~3 in-game days

    public static float IntensityAtTick(long tickNumber)
    {
        var seconds = GameClock.InGameSecondsAt(tickNumber);
        var phase = (seconds % CyclePeriodSeconds) / CyclePeriodSeconds;
        if (phase < 0) phase += 1.0;
        // Soft pulse: cosine that peaks at ~0.5, with the rainy slice
        // being only the top quarter so most ticks read zero.
        var c = Math.Cos((phase - 0.5) * 2.0 * Math.PI);
        var raw = (c - 0.5) * 2.0;
        return (float)Math.Clamp(raw, 0.0, 1.0);
    }
}
