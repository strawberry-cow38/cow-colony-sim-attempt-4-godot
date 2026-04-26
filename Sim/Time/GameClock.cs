namespace CowColonySim.Sim.Time;

public static class GameClock
{
    // World epoch: 1999-01-01 06:00 local. tick 0 maps to this instant.
    public static readonly DateTime Epoch =
        new(1999, 1, 1, 6, 0, 0, DateTimeKind.Unspecified);

    // 24 IRL minutes = 1 in-game day → 60 in-game seconds per IRL second
    // at 1× speed. With TickRateHz = 60 that comes out to exactly one
    // in-game second per tick, regardless of the speed multiplier
    // (speed multiplies tick rate, not the per-tick advance).
    public const double InGameSecondsPerTick =
        SimConstants.InGameSecondsPerIRLSec * SimConstants.FixedDeltaSeconds;

    // Wall-clock seconds since launch — used for HUD elapsed counters and
    // tests. NOT the in-game time; for that, use InGameSecondsAt /
    // DateTimeAt below.
    public static double SecondsAt(long tickNumber) =>
        tickNumber * SimConstants.FixedDeltaSeconds;

    public static long TickAtSeconds(double seconds) =>
        (long)Math.Round(seconds * SimConstants.TickRateHz);

    public static double InGameSecondsAt(long tickNumber) =>
        tickNumber * InGameSecondsPerTick;

    public static DateTime DateTimeAt(long tickNumber) =>
        Epoch.AddSeconds(InGameSecondsAt(tickNumber));
}
