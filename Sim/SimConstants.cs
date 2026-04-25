namespace CowColonySim.Sim;

public static class SimConstants
{
    public const int TickRateHz = 60;
    public const double FixedDeltaSeconds = 1.0 / TickRateHz;

    public const float MetersPerTile = 1.5f;
    public const float GodotUnitsPerMeter = 28.6666667f;
    public const float GodotUnitsPerTile = 43.0f;
}
