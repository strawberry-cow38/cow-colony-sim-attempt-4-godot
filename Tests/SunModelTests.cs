using CowColonySim.Sim.Lighting;
using CowColonySim.Sim.Map;
using Xunit;

namespace CowColonySim.Tests;

public class SunModelTests
{
    private static readonly DayLightWindow Window = DayLightWindow.Default;

    [Fact]
    public void Pre_dawn_is_zero()
    {
        Assert.Equal(0.0, SunModel.ComputeSunFraction(0.0, Window));
        Assert.Equal(0.0, SunModel.ComputeSunFraction(Window.DawnStart - 0.001, Window));
    }

    [Fact]
    public void Mid_dawn_is_half()
    {
        var mid = (Window.DawnStart + Window.DawnEnd) / 2.0;
        Assert.Equal(0.5, SunModel.ComputeSunFraction(mid, Window), precision: 4);
    }

    [Fact]
    public void Midday_is_full()
    {
        Assert.Equal(1.0, SunModel.ComputeSunFraction(0.5, Window));
    }

    [Fact]
    public void Mid_dusk_is_half()
    {
        var mid = (Window.DuskStart + Window.DuskEnd) / 2.0;
        Assert.Equal(0.5, SunModel.ComputeSunFraction(mid, Window), precision: 4);
    }

    [Fact]
    public void After_dusk_is_zero()
    {
        Assert.Equal(0.0, SunModel.ComputeSunFraction(0.99, Window));
    }

    [Fact]
    public void Sun_byte_at_midday_is_max()
    {
        Assert.Equal(LightConstants.Max, SunModel.ComputeSunByte(0.5, Window));
    }
}
