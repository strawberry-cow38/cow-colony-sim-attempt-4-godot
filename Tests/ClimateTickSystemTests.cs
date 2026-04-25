using CowColonySim.Sim.Climate;
using CowColonySim.Sim.Map;
using CowColonySim.Sim.Systems;
using Xunit;

namespace CowColonySim.Tests;

public class ClimateTickSystemTests
{
    [Fact]
    public void Tick_publishes_snapshot_with_global_temp_and_wind()
    {
        var settings = new MapSettings(Width: 8, Height: 8, MinZ: 0, MaxZ: 4, Seed: 42);
        var state = new ClimateState();
        Assert.Same(ClimateSnapshot.Empty, state.Current);

        var sys = new ClimateTickSystem(settings, state);
        var ctx = new TickContext(tickNumber: 0, fixedDelta: 1.0 / 60.0);
        sys.Tick(in ctx);

        var snap = state.Current;
        Assert.NotSame(ClimateSnapshot.Empty, snap);
        Assert.Equal(Biome.TemperateForest, snap.Biome);
        Assert.InRange(snap.WindDegrees, 0.0, 360.0);
        Assert.InRange(snap.WindSpeedMps, WindModel.SpeedMin, WindModel.SpeedMax);
        Assert.Equal(WindCategoryHelper.FromSpeed(snap.WindSpeedMps), snap.WindCategory);
        Assert.Equal(CompassHelper.FromDegrees(snap.WindDegrees), snap.WindDirection);
    }

    [Fact]
    public void Biome_setting_flows_through_to_snapshot()
    {
        var settings = new MapSettings(Biome: Biome.Desert);
        var state = new ClimateState();
        var sys = new ClimateTickSystem(settings, state);
        sys.Tick(new TickContext(0, 1.0 / 60.0));
        Assert.Equal(Biome.Desert, state.Current.Biome);
    }
}
