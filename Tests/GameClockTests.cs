using CowColonySim.Sim;
using CowColonySim.Sim.Time;
using Xunit;

namespace CowColonySim.Tests;

public class GameClockTests
{
    [Fact]
    public void Tick_zero_is_epoch_8am()
    {
        var dt = GameClock.ToDateTime(0);
        Assert.Equal(new DateTime(1999, 1, 1, 8, 0, 0, DateTimeKind.Utc), dt);
        Assert.Equal(8.0 / 24.0, GameClock.DayFraction(0), precision: 6);
        Assert.Equal(0, GameClock.DayIndex(0));
    }

    [Fact]
    public void Sixteen_hours_after_epoch_is_midnight_day_one()
    {
        var ticks = 16 * 3600;
        Assert.Equal(0.0, GameClock.DayFraction(ticks), precision: 6);
        Assert.Equal(1, GameClock.DayIndex(ticks));
        Assert.Equal(new DateTime(1999, 1, 2, 0, 0, 0, DateTimeKind.Utc), GameClock.ToDateTime(ticks));
    }

    [Fact]
    public void TicksPerDay_resolves_to_86400_at_one_second_per_tick()
    {
        Assert.Equal(86_400, CalendarConstants.TicksPerDay);
        var startOfDay = GameClock.ToDateTime(0);
        var endOfDay = GameClock.ToDateTime(CalendarConstants.TicksPerDay);
        Assert.Equal(TimeSpan.FromDays(1), endOfDay - startOfDay);
    }

    [Fact]
    public void One_irl_minute_at_one_x_advances_60_game_seconds_per_irl_second()
    {
        // sanity: 24 IRL min/day at 1x => 1 IRL sec = 60 game sec
        var ticksPerIrlSecond = SimConstants.TickRateHz; // 60
        var gameSecondsAdvanced = ticksPerIrlSecond * CalendarConstants.GameSecondsPerTick;
        Assert.Equal(60.0, gameSecondsAdvanced);
        Assert.Equal(CalendarConstants.IrlSecondsPerGameDayAt1x,
            CalendarConstants.GameSecondsPerDay / gameSecondsAdvanced);
    }
}
