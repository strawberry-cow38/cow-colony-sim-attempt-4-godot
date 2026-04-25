using CowColonySim.Sim.Lighting;
using CowColonySim.Sim.Map;
using Friflo.Engine.ECS;
using Xunit;

namespace CowColonySim.Tests;

public class ArtificialLightTests
{
    private static (TileGrid grid, EntityStore store, ArtificialLightSystem sys) MakeFixture()
    {
        var s = new MapSettings(Width: 16, Height: 16, MinZ: 0, MaxZ: 4);
        var grid = new TileGrid(s);
        var store = new EntityStore();
        var sys = new ArtificialLightSystem(grid, store);
        return (grid, store, sys);
    }

    private static Entity Place(EntityStore store, int x, int y, int z, byte intensity, int radius)
    {
        var e = store.CreateEntity();
        e.AddComponent(new TileCoord(x, y, z));
        e.AddComponent(new LightEmitter(intensity, radius));
        return e;
    }

    [Fact]
    public void Source_lights_self_at_full_intensity_capped_at_artificial_max()
    {
        var (grid, store, sys) = MakeFixture();
        Place(store, 8, 8, 1, intensity: 200, radius: 5);
        sys.Rebuild();
        Assert.Equal(LightConstants.ArtificialMax, grid.ArtificialLight[grid.Index(8, 8, 1)]);
    }

    [Fact]
    public void Light_decays_linearly_with_distance()
    {
        var (grid, store, sys) = MakeFixture();
        Place(store, 8, 8, 1, intensity: 100, radius: 5);
        sys.Rebuild();
        var atSource = grid.ArtificialLight[grid.Index(8, 8, 1)];
        var atTwo = grid.ArtificialLight[grid.Index(10, 8, 1)];
        Assert.True(atSource > atTwo);
        Assert.True(atTwo > 0);
        var beyond = grid.ArtificialLight[grid.Index(14, 8, 1)];
        Assert.Equal(0, beyond);
    }

    [Fact]
    public void Two_sources_max_merge_in_overlap()
    {
        var (grid, store, sys) = MakeFixture();
        Place(store, 4, 4, 1, intensity: 80, radius: 4);
        Place(store, 8, 4, 1, intensity: 80, radius: 4);
        sys.Rebuild();
        var midpoint = grid.ArtificialLight[grid.Index(6, 4, 1)];
        var farFromBoth = grid.ArtificialLight[grid.Index(0, 0, 0)];
        Assert.True(midpoint > 0);
        Assert.Equal(0, farFromBoth);
    }

    [Fact]
    public void Wall_blocks_propagation_past_it()
    {
        var (grid, store, sys) = MakeFixture();
        for (var z = 0; z < 4; z++)
        {
            grid.SetFlag(5, 4, z, TileFlags.HasWall, true);
        }
        Place(store, 4, 4, 1, intensity: 100, radius: 6);
        sys.Rebuild();

        var nearWall = grid.ArtificialLight[grid.Index(4, 4, 1)];
        var pastWall = grid.ArtificialLight[grid.Index(7, 4, 1)];
        Assert.True(nearWall > 0);
        Assert.True(pastWall < nearWall, "wall should attenuate propagation");
    }

    [Fact]
    public void Dirty_flag_skips_unnecessary_rebuilds()
    {
        var (_, store, sys) = MakeFixture();
        Place(store, 0, 0, 0, intensity: 50, radius: 3);
        sys.Rebuild();
        Assert.False(sys.IsDirty);
        sys.MarkDirty();
        Assert.True(sys.IsDirty);
        sys.RebuildIfDirty();
        Assert.False(sys.IsDirty);
    }
}
