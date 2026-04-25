using CowColonySim.Sim.Terrain;
using Xunit;

namespace CowColonySim.Tests;

public class HeightfieldTests
{
    [Fact]
    public void Vert_grid_is_one_larger_than_tile_grid()
    {
        var f = new Heightfield(tileWidth: 16, tileHeight: 8);
        Assert.Equal(17, f.VertWidth);
        Assert.Equal(9, f.VertHeight);
    }

    [Fact]
    public void Set_clamps_to_quanta_range()
    {
        var f = new Heightfield(4, 4);
        f.Set(0, 0, (short)(TerrainConstants.MaxQuanta + 100));
        f.Set(1, 0, (short)(TerrainConstants.MinQuanta - 100));
        Assert.Equal(TerrainConstants.MaxQuanta, f.Get(0, 0));
        Assert.Equal(TerrainConstants.MinQuanta, f.Get(1, 0));
    }

    [Fact]
    public void Version_bumps_only_on_actual_change()
    {
        var f = new Heightfield(4, 4);
        var v0 = f.Version;
        f.Set(2, 2, 5);
        var v1 = f.Version;
        Assert.True(v1 > v0);
        f.Set(2, 2, 5);
        Assert.Equal(v1, f.Version);
    }

    [Fact]
    public void Metres_are_quanta_times_resolution()
    {
        var f = new Heightfield(4, 4);
        f.Set(1, 1, 4);
        Assert.Equal(4 * TerrainConstants.VerticalQuantumMetres, f.MetresAt(1, 1));
    }

    [Fact]
    public void In_bounds_rejects_negative_and_overflow()
    {
        var f = new Heightfield(4, 4);
        Assert.True(f.InBounds(0, 0));
        Assert.True(f.InBounds(4, 4));
        Assert.False(f.InBounds(-1, 0));
        Assert.False(f.InBounds(5, 0));
        Assert.False(f.InBounds(0, 5));
    }
}
