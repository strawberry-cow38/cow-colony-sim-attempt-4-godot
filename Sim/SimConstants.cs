namespace CowColonySim.Sim;

public static class SimConstants
{
    public const int TickRateHz = 60;
    public const double FixedDeltaSeconds = 1.0 / TickRateHz;

    // 1 tile = 1.5 m = 43 Godot units. Sim stores tile coords + sub-tile
    // floats; Game multiplies by GodotUnitsPerTile when rendering.
    public const float MetersPerTile = 1.5f;
    public const float GodotUnitsPerTile = 43f;

    // 24 IRL minutes per in-game day. 1 IRL second = 60 in-game seconds.
    public const double IRLMinutesPerInGameDay = 24.0;
    public const double InGameSecondsPerIRLSec =
        86400.0 / (IRLMinutesPerInGameDay * 60.0);
}
