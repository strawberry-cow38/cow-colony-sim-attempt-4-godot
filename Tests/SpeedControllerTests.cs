using CowColonySim.Sim;
using CowColonySim.Sim.Time;
using Xunit;

namespace CowColonySim.Tests;

public class SpeedControllerTests
{
    [Fact]
    public void Default_speed_is_normal()
    {
        var s = new SpeedController();
        Assert.Equal(SimSpeed.Normal, s.Current);
        Assert.False(s.IsPaused);
        Assert.Equal(SimConstants.TickRateHz, s.TargetTicksPerSecond);
    }

    [Theory]
    [InlineData(SimSpeed.Paused, 0)]
    [InlineData(SimSpeed.Normal, 60)]
    [InlineData(SimSpeed.Fast, 120)]
    [InlineData(SimSpeed.VeryFast, 180)]
    [InlineData(SimSpeed.UltraFast, 360)]
    public void Target_tps_scales_with_multiplier(SimSpeed speed, int expectedTps)
    {
        var s = new SpeedController { Current = speed };
        Assert.Equal(expectedTps, s.TargetTicksPerSecond);
    }

    [Fact]
    public void Toggle_pause_round_trips_to_normal()
    {
        var s = new SpeedController();
        s.TogglePause();
        Assert.True(s.IsPaused);
        s.TogglePause();
        Assert.Equal(SimSpeed.Normal, s.Current);
    }
}
