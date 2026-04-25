using CowColonySim.Sim.Climate;
using Xunit;

namespace CowColonySim.Tests;

public class SeasonTests
{
    [Theory]
    [InlineData(1, Season.Winter)]
    [InlineData(4, Season.Spring)]
    [InlineData(7, Season.Summer)]
    [InlineData(10, Season.Autumn)]
    [InlineData(12, Season.Winter)]
    public void Northern_hemisphere_seasons_match_calendar(int month, Season expected)
    {
        var date = new DateTime(2026, month, 15, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(expected, SeasonHelper.FromDate(date, latitude: 45.0));
    }

    [Theory]
    [InlineData(1, Season.Summer)]
    [InlineData(4, Season.Autumn)]
    [InlineData(7, Season.Winter)]
    [InlineData(10, Season.Spring)]
    public void Southern_hemisphere_flips_seasons(int month, Season expected)
    {
        var date = new DateTime(2026, month, 15, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(expected, SeasonHelper.FromDate(date, latitude: -33.0));
    }
}
