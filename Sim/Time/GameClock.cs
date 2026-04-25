namespace CowColonySim.Sim.Time;

public static class GameClock
{
    public static DateTime ToDateTime(long tickNumber)
    {
        var totalSeconds = tickNumber * CalendarConstants.GameSecondsPerTick;
        return CalendarConstants.Epoch.AddSeconds(totalSeconds);
    }

    public static double DayFraction(long tickNumber)
    {
        var dt = ToDateTime(tickNumber);
        var sinceMidnight = dt.TimeOfDay.TotalSeconds;
        return sinceMidnight / CalendarConstants.GameSecondsPerDay;
    }

    public static int DayIndex(long tickNumber)
    {
        var elapsedDays = (ToDateTime(tickNumber) - CalendarConstants.Epoch.Date).TotalDays;
        return (int)Math.Floor(elapsedDays);
    }
}
