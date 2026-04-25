namespace CowColonySim.Sim.Climate;

// Deterministic wind from (seed, tick). Smooth, low-frequency. No randomness:
// same (seed, tick) → same (degrees, speed). One game-second per tick, so we
// drive in hours-of-game-time units to keep it slow.
public static class WindModel
{
    private const double SecondsPerHour = 3600.0;

    public const double SpeedMin = 0.0;
    public const double SpeedMax = 15.0;
    public const double SpeedBase = 7.5;

    public static double DirectionDegrees(int seed, long tick)
    {
        var hours = tick / SecondsPerHour;
        var seedOffset = seed * 0.13;
        var slow = Math.Sin(seedOffset + hours * 0.05);
        var fast = 0.35 * Math.Sin(seedOffset * 1.7 + hours * 0.21);
        var raw = 180.0 + 180.0 * (slow + fast);
        return ((raw % 360.0) + 360.0) % 360.0;
    }

    public static double SpeedMetresPerSecond(int seed, long tick)
    {
        var hours = tick / SecondsPerHour;
        var seedOffset = seed * 0.27;
        var swing = 5.0 * Math.Sin(seedOffset + hours * 0.07)
                  + 3.0 * Math.Sin(hours * 0.2 + seedOffset * 0.5);
        var s = SpeedBase + swing;
        if (s < SpeedMin) return SpeedMin;
        if (s > SpeedMax) return SpeedMax;
        return s;
    }
}
