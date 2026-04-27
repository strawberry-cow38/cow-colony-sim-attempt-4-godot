namespace CowColonySim.Sim.Weather;

// Per-map climate config. Drives the seasonal + daily temperature
// curve and the per-month distribution of annual rainfall. Single
// global default for now; per-map / per-biome overrides slot in here
// when worldgen lands.
public sealed class MapClimate
{
    public float AnnualMeanCelsius { get; init; } = 13f;
    public float AnnualAmplitudeC { get; init; } = 10f;
    public float DailyAmplitudeC { get; init; } = 5f;
    public int PeakSummerDayOfYear { get; init; } = 196;

    public float AnnualRainfallMm { get; init; } = 800f;

    // Share of annual rainfall per month (Jan..Dec). Should sum to ~1.
    // Default is a temperate maritime profile (wetter autumn/winter).
    public IReadOnlyList<float> MonthlyShare { get; init; } = new float[]
    {
        0.10f, 0.08f, 0.07f, 0.06f, 0.06f, 0.06f,
        0.07f, 0.07f, 0.08f, 0.10f, 0.12f, 0.13f,
    };

    public static MapClimate Temperate { get; } = new();
}
