using CowColonySim.Sim.Lighting;
using CowColonySim.Sim.Time;

namespace CowColonySim.Sim.Weather;

// Maps tick → ambient temperature in Celsius. Combines a yearly
// seasonal cosine (peaks at climate.PeakSummerDayOfYear, troughs half
// a year off) with a daily swing driven by SunCurve. Future: biome
// offsets, indoor heating, weather-front deltas.
public static class TempCurve
{
    public static float CelsiusAtTick(long tickNumber, MapClimate climate)
    {
        var dt = GameClock.DateTimeAt(tickNumber);
        var seasonPhase = (dt.DayOfYear - climate.PeakSummerDayOfYear) / 365.0;
        var seasonal = climate.AnnualMeanCelsius
            + climate.AnnualAmplitudeC * (float)Math.Cos(seasonPhase * 2.0 * Math.PI);
        var sun = SunCurve.FractionAtTick(tickNumber);
        var daily = climate.DailyAmplitudeC * (sun - 0.5f) * 2f;
        return seasonal + daily;
    }
}
