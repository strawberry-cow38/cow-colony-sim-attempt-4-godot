using CowColonySim.Sim.Climate;
using Xunit;

namespace CowColonySim.Tests;

public class WindCategoryTests
{
    [Theory]
    [InlineData(0.0, WindCategory.Calm)]
    [InlineData(0.9, WindCategory.Calm)]
    [InlineData(1.0, WindCategory.Breeze)]
    [InlineData(4.9, WindCategory.Breeze)]
    [InlineData(5.0, WindCategory.Moderate)]
    [InlineData(8.9, WindCategory.Moderate)]
    [InlineData(9.0, WindCategory.Strong)]
    [InlineData(12.9, WindCategory.Strong)]
    [InlineData(13.0, WindCategory.Gale)]
    [InlineData(40.0, WindCategory.Gale)]
    public void Speed_maps_to_simplified_category(double mps, WindCategory expected)
    {
        Assert.Equal(expected, WindCategoryHelper.FromSpeed(mps));
    }
}
