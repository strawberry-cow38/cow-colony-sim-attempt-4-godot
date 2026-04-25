using CowColonySim.Sim.Map;

namespace CowColonySim.Sim.Lighting;

public static class SunModel
{
    public static byte ComputeSunByte(double dayFraction, DayLightWindow window)
    {
        return (byte)Math.Round(ComputeSunFraction(dayFraction, window) * LightConstants.Max);
    }

    public static double ComputeSunFraction(double dayFraction, DayLightWindow window)
    {
        var f = ((dayFraction % 1.0) + 1.0) % 1.0;

        if (f < window.DawnStart || f >= window.DuskEnd)
        {
            return 0.0;
        }
        if (f < window.DawnEnd)
        {
            var span = window.DawnEnd - window.DawnStart;
            return span <= 0 ? 1.0 : (f - window.DawnStart) / span;
        }
        if (f < window.DuskStart)
        {
            return 1.0;
        }
        var duskSpan = window.DuskEnd - window.DuskStart;
        return duskSpan <= 0 ? 0.0 : 1.0 - (f - window.DuskStart) / duskSpan;
    }
}
