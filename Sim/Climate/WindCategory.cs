namespace CowColonySim.Sim.Climate;

public enum WindCategory : byte
{
    Calm = 0,
    Breeze,
    Moderate,
    Strong,
    Gale,
}

public static class WindCategoryHelper
{
    // Simplified Beaufort-ish thresholds in metres/second.
    public const double BreezeMin = 1.0;
    public const double ModerateMin = 5.0;
    public const double StrongMin = 9.0;
    public const double GaleMin = 13.0;

    public static WindCategory FromSpeed(double metresPerSecond) =>
        metresPerSecond switch
        {
            < BreezeMin => WindCategory.Calm,
            < ModerateMin => WindCategory.Breeze,
            < StrongMin => WindCategory.Moderate,
            < GaleMin => WindCategory.Strong,
            _ => WindCategory.Gale,
        };
}
