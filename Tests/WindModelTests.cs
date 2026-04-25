using CowColonySim.Sim.Climate;
using Xunit;

namespace CowColonySim.Tests;

public class WindModelTests
{
    [Fact]
    public void Same_seed_and_tick_yield_same_wind()
    {
        var d1 = WindModel.DirectionDegrees(seed: 42, tick: 12345);
        var d2 = WindModel.DirectionDegrees(seed: 42, tick: 12345);
        var s1 = WindModel.SpeedMetresPerSecond(seed: 42, tick: 12345);
        var s2 = WindModel.SpeedMetresPerSecond(seed: 42, tick: 12345);
        Assert.Equal(d1, d2);
        Assert.Equal(s1, s2);
    }

    [Fact]
    public void Different_seeds_diverge()
    {
        var d1 = WindModel.DirectionDegrees(seed: 1, tick: 0);
        var d2 = WindModel.DirectionDegrees(seed: 2, tick: 0);
        Assert.NotEqual(d1, d2);
    }

    [Fact]
    public void Direction_stays_in_zero_to_three_sixty()
    {
        for (long t = 0; t < 100_000; t += 137)
        {
            var d = WindModel.DirectionDegrees(seed: 7, tick: t);
            Assert.InRange(d, 0.0, 360.0);
        }
    }

    [Fact]
    public void Speed_is_clamped_to_valid_range()
    {
        for (long t = 0; t < 100_000; t += 137)
        {
            var s = WindModel.SpeedMetresPerSecond(seed: 7, tick: t);
            Assert.InRange(s, WindModel.SpeedMin, WindModel.SpeedMax);
        }
    }

    [Fact]
    public void Wind_changes_smoothly_across_neighbouring_ticks()
    {
        // Adjacent ticks (1 game-second apart) should not jump wildly.
        var prev = WindModel.SpeedMetresPerSecond(seed: 3, tick: 1000);
        var next = WindModel.SpeedMetresPerSecond(seed: 3, tick: 1001);
        Assert.True(Math.Abs(next - prev) < 0.1);
    }
}
