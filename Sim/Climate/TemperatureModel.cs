using CowColonySim.Sim.Map;

namespace CowColonySim.Sim.Climate;

// Pure temperature math. Earth-ish ranges in Celsius.
// Composes: mean annual by latitude + seasonal swing + diurnal swing
// + altitude lapse. No biome modifier yet (deferred until world map).
public static class TemperatureModel
{
    // Standard environmental lapse rate: -6.5 K per 1000 m of altitude.
    public const double LapseRatePerMetre = -0.0065;

    // °C diurnal half-amplitude (so total swing is 2x this).
    public const double DiurnalAmplitudeC = 4.0;

    // Day-of-year (1-based) of the northern hemisphere coldest point,
    // used as the seasonal trough; peak summer is half a year later.
    private const double NorthernTroughDoy = 15.0;
    private const double DaysPerYear = 365.25;

    public static double MeanAnnualC(double latitude)
    {
        var absLat = Math.Abs(latitude);
        return 30.0 - 0.45 * absLat;
    }

    public static double SeasonalAmplitudeC(double latitude)
    {
        var absLat = Math.Abs(latitude);
        return 0.25 * absLat;
    }

    // Returns -1..+1; +1 means summer peak, -1 means winter trough.
    public static double SeasonalPhase(DateTime gameTime, double latitude)
    {
        var doy = gameTime.DayOfYear;
        var northern = -Math.Cos(2.0 * Math.PI * (doy - NorthernTroughDoy) / DaysPerYear);
        return latitude < 0 ? -northern : northern;
    }

    public static double DiurnalDeltaC(double dayFraction)
    {
        const double peakFraction = 14.0 / 24.0;
        return DiurnalAmplitudeC * Math.Cos(2.0 * Math.PI * (dayFraction - peakFraction));
    }

    public static double AltitudeDeltaC(int z)
    {
        var metres = z * SimConstants.MetersPerTile;
        return LapseRatePerMetre * metres;
    }

    public static double GlobalSurfaceC(DateTime gameTime, double dayFraction, double latitude)
    {
        var mean = MeanAnnualC(latitude);
        var seasonal = SeasonalAmplitudeC(latitude) * SeasonalPhase(gameTime, latitude);
        var diurnal = DiurnalDeltaC(dayFraction);
        return mean + seasonal + diurnal;
    }

    public static double TileC(double globalSurfaceC, int z) =>
        globalSurfaceC + AltitudeDeltaC(z);
}
