namespace CowColonySim.Sim.Time;

public static class CalendarConstants
{
    public static readonly DateTime Epoch =
        new(1999, 1, 1, 8, 0, 0, DateTimeKind.Utc);

    public const double GameSecondsPerTick = 1.0;

    public const int GameSecondsPerDay = 86_400;

    public const int TicksPerDay =
        (int)(GameSecondsPerDay / GameSecondsPerTick);

    public const double IrlSecondsPerGameDayAt1x = 24 * 60;
}
