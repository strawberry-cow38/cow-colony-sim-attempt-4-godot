using CowColonySim.Sim.Time;

namespace CowColonySim.Sim.Weather;

// Maps tick → rainfall intensity [0..1] consistent with the map's
// configured annual rainfall (mm) and per-month distribution. Each
// in-game day gets a soft afternoon pulse whose peak intensity is
// scaled by the day's expected millimetres so wetter months actually
// rain harder. Stochastic fronts replace this later.
public static class RainCurve
{
    private const float ReferenceMaxDailyMm = 30f;
    private const double PeakHourFraction = 15.0 / 24.0;

    public static float IntensityAtTick(long tickNumber, MapClimate climate)
    {
        var dt = GameClock.DateTimeAt(tickNumber);
        var monthIdx = dt.Month - 1;
        var daysInMonth = DateTime.DaysInMonth(dt.Year, dt.Month);
        var share = climate.MonthlyShare[monthIdx];
        var monthMm = climate.AnnualRainfallMm * share;
        var dailyMm = monthMm / daysInMonth;
        var normalized = MathF.Min(1f, dailyMm / ReferenceMaxDailyMm);

        var hourFraction = (dt.Hour + dt.Minute / 60.0 + dt.Second / 3600.0) / 24.0;
        var c = Math.Cos((hourFraction - PeakHourFraction) * 2.0 * Math.PI);
        var pulse = (c - 0.5) * 2.0;
        if (pulse <= 0) return 0f;
        return (float)(pulse * normalized);
    }
}
