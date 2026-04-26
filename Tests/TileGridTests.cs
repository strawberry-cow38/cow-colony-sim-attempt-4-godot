using CowColonySim.Sim.Map;
using Xunit;

namespace CowColonySim.Tests;

public class TileGridTests
{
    [Fact]
    public void Default_settings_match_phase_target()
    {
        var s = new MapSettings();
        Assert.Equal(256, s.Width);
        Assert.Equal(256, s.Height);
        Assert.Equal(5, s.Depth);
    }

    [Fact]
    public void Allocates_flags_for_full_volume()
    {
        var grid = new TileGrid(new MapSettings(Width: 8, Height: 4, MinZ: 0, MaxZ: 2));
        Assert.Equal(8 * 4 * 3, grid.Flags.Length);
        foreach (var f in grid.Flags)
        {
            Assert.Equal(TileFlags.None, f);
        }
    }

    [Fact]
    public void Index_is_unique_per_tile()
    {
        var grid = new TileGrid(new MapSettings(Width: 4, Height: 3, MinZ: -1, MaxZ: 1));
        var seen = new HashSet<int>();
        for (var z = grid.MinZ; z <= grid.MaxZ; z++)
        {
            for (var y = 0; y < grid.Height; y++)
            {
                for (var x = 0; x < grid.Width; x++)
                {
                    Assert.True(seen.Add(grid.Index(x, y, z)));
                }
            }
        }
        Assert.Equal(grid.Flags.Length, seen.Count);
    }

    [Fact]
    public void InBounds_rejects_out_of_range()
    {
        var grid = new TileGrid(new MapSettings(Width: 4, Height: 4, MinZ: 0, MaxZ: 2));
        Assert.True(grid.InBounds(0, 0, 0));
        Assert.True(grid.InBounds(3, 3, 2));
        Assert.False(grid.InBounds(-1, 0, 0));
        Assert.False(grid.InBounds(0, 4, 0));
        Assert.False(grid.InBounds(0, 0, -1));
        Assert.False(grid.InBounds(0, 0, 3));
    }

    [Fact]
    public void Flags_are_combinable()
    {
        var f = TileFlags.Solid | TileFlags.ExposedToSky;
        Assert.True((f & TileFlags.Solid) != 0);
        Assert.True((f & TileFlags.ExposedToSky) != 0);
        Assert.False((f & TileFlags.Water) != 0);
    }

    [Fact]
    public void Negative_size_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new TileGrid(new MapSettings(Width: 0, Height: 4)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new TileGrid(new MapSettings(MinZ: 5, MaxZ: 0)));
    }
}
