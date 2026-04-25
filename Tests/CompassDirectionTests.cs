using CowColonySim.Sim.Climate;
using Xunit;

namespace CowColonySim.Tests;

public class CompassDirectionTests
{
    [Theory]
    [InlineData(0.0, CompassDirection.N)]
    [InlineData(22.0, CompassDirection.N)]
    [InlineData(23.0, CompassDirection.NE)]
    [InlineData(45.0, CompassDirection.NE)]
    [InlineData(90.0, CompassDirection.E)]
    [InlineData(135.0, CompassDirection.SE)]
    [InlineData(180.0, CompassDirection.S)]
    [InlineData(225.0, CompassDirection.SW)]
    [InlineData(270.0, CompassDirection.W)]
    [InlineData(315.0, CompassDirection.NW)]
    [InlineData(359.0, CompassDirection.N)]
    [InlineData(720.0, CompassDirection.N)]
    [InlineData(-45.0, CompassDirection.NW)]
    public void Degrees_map_to_eight_compass_sectors(double degrees, CompassDirection expected)
    {
        Assert.Equal(expected, CompassHelper.FromDegrees(degrees));
    }
}
