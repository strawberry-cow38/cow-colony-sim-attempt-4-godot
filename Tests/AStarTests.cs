using CowColonySim.Sim.Pathfinding;
using CowColonySim.Sim.Terrain;
using Xunit;

namespace CowColonySim.Tests;

public class AStarTests
{
    [Fact]
    public void Finds_straight_path_on_flat_terrain()
    {
        var field = new Heightfield(8, 8);
        var grid = new HeightGrid(field);
        var path = new List<TileCoord>();

        var ok = AStar.TryFind(grid, new TileCoord(0, 0), new TileCoord(7, 0), path);

        Assert.True(ok);
        Assert.Equal(new TileCoord(0, 0), path[0]);
        Assert.Equal(new TileCoord(7, 0), path[^1]);
        Assert.Equal(8, path.Count);
    }

    [Fact]
    public void Diagonal_path_has_octile_length()
    {
        var field = new Heightfield(8, 8);
        var grid = new HeightGrid(field);
        var path = new List<TileCoord>();

        var ok = AStar.TryFind(grid, new TileCoord(0, 0), new TileCoord(5, 5), path);

        Assert.True(ok);
        Assert.Equal(new TileCoord(0, 0), path[0]);
        Assert.Equal(new TileCoord(5, 5), path[^1]);
        Assert.Equal(6, path.Count);
    }

    [Fact]
    public void Returns_single_tile_when_start_equals_goal()
    {
        var field = new Heightfield(4, 4);
        var grid = new HeightGrid(field);
        var path = new List<TileCoord>();

        var ok = AStar.TryFind(grid, new TileCoord(2, 2), new TileCoord(2, 2), path);

        Assert.True(ok);
        Assert.Single(path);
        Assert.Equal(new TileCoord(2, 2), path[0]);
    }

    [Fact]
    public void Routes_around_a_cliff()
    {
        var field = new Heightfield(8, 8);
        // Build a vertical wall at x=4 from y=0..6, leaving a gap at y=7.
        // Wall vertices straddle x=4 and x=5; raise tile centres to be
        // unwalkable by setting both border verts high.
        for (var vy = 0; vy <= 6; vy++)
        {
            field.Set(4, vy, 30);
            field.Set(5, vy, 30);
        }
        var grid = new HeightGrid(field);
        var path = new List<TileCoord>();

        var ok = AStar.TryFind(grid, new TileCoord(0, 0), new TileCoord(7, 0), path);

        Assert.True(ok);
        Assert.Equal(new TileCoord(0, 0), path[0]);
        Assert.Equal(new TileCoord(7, 0), path[^1]);
        // Must dip down toward y=7 to clear the cliff.
        Assert.Contains(path, t => t.Y >= 6);
    }

    [Fact]
    public void Returns_false_when_goal_unreachable()
    {
        var field = new Heightfield(6, 6);
        // Full vertical wall at x=3, no gap.
        for (var vy = 0; vy <= 6; vy++)
        {
            field.Set(3, vy, 60);
            field.Set(4, vy, 60);
        }
        var grid = new HeightGrid(field);
        var path = new List<TileCoord>();

        var ok = AStar.TryFind(grid, new TileCoord(0, 0), new TileCoord(5, 0), path);

        Assert.False(ok);
        Assert.Empty(path);
    }
}
