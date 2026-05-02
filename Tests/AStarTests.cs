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
    public void Path_z_components_match_floor_layer_on_sloped_terrain()
    {
        var field = new Heightfield(8, 8);
        // Plateau on the right half: vertices x=4..7 raised by 2 quanta.
        for (var vy = 0; vy < 8; vy++)
        {
            for (var vx = 4; vx < 8; vx++)
            {
                field.Set(vx, vy, 2);
            }
        }
        var grid = new HeightGrid(field);
        var path = new List<TileCoord>();

        var start = grid.At(0, 0);
        var goal = grid.At(6, 0);
        var ok = AStar.TryFind(grid, start, goal, path);

        Assert.True(ok);
        foreach (var t in path)
        {
            Assert.Equal(grid.FloorLayer(t.X, t.Y), t.Z);
        }
        Assert.Contains(path, t => t.Z > 0);
    }

    [Fact]
    public void Climbs_ladder_to_reach_wall_top()
    {
        var field = new Heightfield(6, 1);
        var grid = new HeightGrid(field);
        // Wall at (2,0): blocks ground, walkable top at z=2.
        grid.MarkBlocked(2, 0, true);
        grid.AddWalkableLayer(2, 0, 2);
        // Ladder at (1,0): spans z=0..z=2, ground stays walkable, top is dismountable.
        grid.AddLadder(1, 0, 0, 2);
        grid.AddWalkableLayer(1, 0, 2);

        var path = new List<TileCoord>();
        var ok = AStar.TryFind(grid, new TileCoord(0, 0, 0), new TileCoord(2, 0, 2), path);

        Assert.True(ok);
        Assert.Equal(new TileCoord(0, 0, 0), path[0]);
        Assert.Equal(new TileCoord(2, 0, 2), path[^1]);
        Assert.Contains(new TileCoord(1, 0, 0), path);
        Assert.Contains(new TileCoord(1, 0, 2), path);
    }

    [Fact]
    public void Cannot_reach_wall_top_without_ladder()
    {
        var field = new Heightfield(6, 1);
        var grid = new HeightGrid(field);
        grid.MarkBlocked(2, 0, true);
        grid.AddWalkableLayer(2, 0, 2);

        var path = new List<TileCoord>();
        var ok = AStar.TryFind(grid, new TileCoord(0, 0, 0), new TileCoord(2, 0, 2), path);

        Assert.False(ok);
    }

    [Fact]
    public void Routes_over_wall_via_paired_ladders()
    {
        var field = new Heightfield(6, 1);
        var grid = new HeightGrid(field);
        // 1-tall corridor: only path is over the wall via ladders.
        grid.MarkBlocked(2, 0, true);
        grid.AddWalkableLayer(2, 0, 2);
        grid.AddLadder(1, 0, 0, 2);
        grid.AddWalkableLayer(1, 0, 2);
        grid.AddLadder(3, 0, 0, 2);
        grid.AddWalkableLayer(3, 0, 2);

        var path = new List<TileCoord>();
        var ok = AStar.TryFind(grid, new TileCoord(0, 0, 0), new TileCoord(4, 0, 0), path);

        Assert.True(ok);
        Assert.Contains(new TileCoord(2, 0, 2), path);
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
