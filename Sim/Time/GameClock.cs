namespace CowColonySim.Sim.Time;

public static class GameClock
{
    public static double SecondsAt(long tickNumber) =>
        tickNumber * SimConstants.FixedDeltaSeconds;

    public static long TickAtSeconds(double seconds) =>
        (long)Math.Round(seconds * SimConstants.TickRateHz);
}
