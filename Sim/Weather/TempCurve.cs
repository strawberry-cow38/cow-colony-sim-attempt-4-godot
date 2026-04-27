using CowColonySim.Sim.Lighting;

namespace CowColonySim.Sim.Weather;

// Maps tick → ambient temperature in Celsius. Today this is a simple
// lerp over the same SunCurve fraction the lighting uses, so the
// coldest moment is deep night and the warmest is solar noon. Future:
// seasonal curve, biome offsets, indoor heating.
public static class TempCurve
{
    private const float NightLowC = 5f;
    private const float DayHighC  = 22f;

    public static float CelsiusAtTick(long tickNumber)
    {
        var sun = SunCurve.FractionAtTick(tickNumber);
        return NightLowC + (DayHighC - NightLowC) * sun;
    }
}
