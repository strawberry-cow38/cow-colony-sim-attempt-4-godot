using CowColonySim.Sim.Climate;
using Xunit;

namespace CowColonySim.Tests;

public class TemperatureModelTests
{
    private static DateTime MidJulyNoon => new(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc);
    private static DateTime MidJanuaryNoon => new(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Equator_is_hotter_on_average_than_poles()
    {
        var equator = TemperatureModel.MeanAnnualC(0.0);
        var temperate = TemperatureModel.MeanAnnualC(45.0);
        var polar = TemperatureModel.MeanAnnualC(80.0);
        Assert.True(equator > temperate);
        Assert.True(temperate > polar);
    }

    [Fact]
    public void Seasonal_amplitude_grows_with_latitude()
    {
        Assert.Equal(0.0, TemperatureModel.SeasonalAmplitudeC(0.0));
        Assert.True(TemperatureModel.SeasonalAmplitudeC(60.0)
                  > TemperatureModel.SeasonalAmplitudeC(30.0));
    }

    [Fact]
    public void Northern_summer_warmer_than_winter_at_same_lat()
    {
        var lat = 45.0;
        var summer = TemperatureModel.GlobalSurfaceC(MidJulyNoon, 0.5, lat);
        var winter = TemperatureModel.GlobalSurfaceC(MidJanuaryNoon, 0.5, lat);
        Assert.True(summer > winter);
    }

    [Fact]
    public void Southern_hemisphere_seasons_invert()
    {
        var lat = -45.0;
        var jul = TemperatureModel.GlobalSurfaceC(MidJulyNoon, 0.5, lat);
        var jan = TemperatureModel.GlobalSurfaceC(MidJanuaryNoon, 0.5, lat);
        Assert.True(jan > jul);
    }

    [Fact]
    public void Diurnal_swing_peaks_in_afternoon_troughs_predawn()
    {
        var afternoon = TemperatureModel.DiurnalDeltaC(14.0 / 24.0);
        var predawn = TemperatureModel.DiurnalDeltaC(2.0 / 24.0);
        Assert.True(afternoon > predawn);
        Assert.Equal(TemperatureModel.DiurnalAmplitudeC, afternoon, 6);
    }

    [Fact]
    public void Higher_altitude_is_colder()
    {
        var ground = TemperatureModel.TileC(20.0, z: 0);
        var mountain = TemperatureModel.TileC(20.0, z: 100);
        Assert.True(ground > mountain);
    }

    [Fact]
    public void Altitude_lapse_uses_standard_rate()
    {
        var deltaPerTile = TemperatureModel.AltitudeDeltaC(1) - TemperatureModel.AltitudeDeltaC(0);
        // 1 tile = 1.5 m, lapse -0.0065 C/m → -0.00975 C per tile.
        Assert.Equal(-0.00975, deltaPerTile, 6);
    }
}
