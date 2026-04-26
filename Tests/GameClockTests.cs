using CowColonySim.Sim;
using CowColonySim.Sim.Time;
using Xunit;

namespace CowColonySim.Tests;

public class GameClockTests
{
    [Fact]
    public void Tick_zero_is_zero_seconds()
    {
        Assert.Equal(0.0, GameClock.SecondsAt(0));
    }

    [Fact]
    public void Sixty_ticks_equals_one_second()
    {
        Assert.Equal(1.0, GameClock.SecondsAt(SimConstants.TickRateHz), precision: 9);
    }

    [Fact]
    public void Round_trips_via_seconds()
    {
        const long input = 12345;
        var seconds = GameClock.SecondsAt(input);
        Assert.Equal(input, GameClock.TickAtSeconds(seconds));
    }
}
