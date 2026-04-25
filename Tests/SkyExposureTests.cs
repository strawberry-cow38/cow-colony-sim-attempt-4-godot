using CowColonySim.Sim.Lighting;
using CowColonySim.Sim.Map;
using Xunit;

namespace CowColonySim.Tests;

public class SkyExposureTests
{
    private static (TileGrid grid, SkyExposureSystem sys) MakeSmallGrid()
    {
        var s = new MapSettings(Width: 4, Height: 4, MinZ: 0, MaxZ: 8);
        var grid = new TileGrid(s);
        var sys = new SkyExposureSystem(grid);
        return (grid, sys);
    }

    [Fact]
    public void Empty_column_is_fully_exposed()
    {
        var (grid, sys) = MakeSmallGrid();
        sys.RebuildAll();
        for (var z = 0; z < 8; z++)
        {
            Assert.True((grid.GetFlags(1, 1, z) & TileFlags.ExposedToSky) != 0);
        }
    }

    [Fact]
    public void Roof_blocks_below_it()
    {
        var (grid, sys) = MakeSmallGrid();
        grid.SetFlag(1, 1, 5, TileFlags.HasRoof, true);
        sys.RebuildAll();

        for (var z = 5; z < 8; z++)
        {
            Assert.True((grid.GetFlags(1, 1, z) & TileFlags.ExposedToSky) != 0,
                $"z={z} above roof should be exposed");
        }
        for (var z = 0; z < 5; z++)
        {
            Assert.False((grid.GetFlags(1, 1, z) & TileFlags.ExposedToSky) != 0,
                $"z={z} below roof should be shielded");
        }
    }

    [Fact]
    public void Floor_blocks_vertical_light()
    {
        var (grid, sys) = MakeSmallGrid();
        grid.SetFlag(2, 2, 4, TileFlags.HasFloor, true);
        sys.RebuildAll();
        Assert.True((grid.GetFlags(2, 2, 4) & TileFlags.ExposedToSky) != 0);
        Assert.False((grid.GetFlags(2, 2, 3) & TileFlags.ExposedToSky) != 0);
    }

    [Fact]
    public void Walls_dont_block_vertical_light()
    {
        var (grid, sys) = MakeSmallGrid();
        grid.SetFlag(0, 0, 5, TileFlags.HasWall, true);
        sys.RebuildAll();
        Assert.True((grid.GetFlags(0, 0, 0) & TileFlags.ExposedToSky) != 0,
            "wall in column should not block sky exposure for cells below");
    }

    [Fact]
    public void Dirty_rebuild_only_processes_marked_columns()
    {
        var (grid, sys) = MakeSmallGrid();
        sys.RebuildAll();
        grid.SetFlag(2, 3, 5, TileFlags.HasRoof, true);
        sys.MarkColumnDirty(2, 3);
        Assert.Equal(1, sys.DirtyColumnCount);
        sys.RebuildDirty();
        Assert.Equal(0, sys.DirtyColumnCount);
        Assert.False((grid.GetFlags(2, 3, 0) & TileFlags.ExposedToSky) != 0);
    }
}
