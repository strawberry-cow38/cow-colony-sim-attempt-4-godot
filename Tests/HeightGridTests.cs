using CowColonySim.Sim.Pathfinding;
using CowColonySim.Sim.Terrain;
using Xunit;

namespace CowColonySim.Tests;

public class HeightGridTests
{
    [Fact]
    public void Extra_walkable_layer_increments_layer_count()
    {
        var field = new Heightfield(4, 4);
        var grid = new HeightGrid(field);
        Assert.Equal(1, grid.LayerCountAt(1, 1));
        grid.AddWalkableLayer(1, 1, 2);
        Assert.Equal(2, grid.LayerCountAt(1, 1));
        Assert.True(grid.HasWalkableLayer(1, 1, 0));
        Assert.True(grid.HasWalkableLayer(1, 1, 2));
    }

    [Fact]
    public void Blocked_tile_reports_only_extra_layers()
    {
        var field = new Heightfield(4, 4);
        var grid = new HeightGrid(field);
        grid.MarkBlocked(1, 1, true);
        grid.AddWalkableLayer(1, 1, 2);
        Assert.Equal(1, grid.LayerCountAt(1, 1));
        Assert.False(grid.HasWalkableLayer(1, 1, 0));
        Assert.True(grid.HasWalkableLayer(1, 1, 2));
        Assert.Equal(2, grid.LayerAt(1, 1, 0));
    }

    [Fact]
    public void Remove_walkable_layer_drops_count()
    {
        var field = new Heightfield(4, 4);
        var grid = new HeightGrid(field);
        grid.AddWalkableLayer(1, 1, 2);
        grid.RemoveWalkableLayer(1, 1, 2);
        Assert.Equal(1, grid.LayerCountAt(1, 1));
        Assert.False(grid.HasWalkableLayer(1, 1, 2));
    }

    [Fact]
    public void Add_walkable_layer_is_idempotent()
    {
        var field = new Heightfield(4, 4);
        var grid = new HeightGrid(field);
        grid.AddWalkableLayer(1, 1, 2);
        grid.AddWalkableLayer(1, 1, 2);
        Assert.Equal(2, grid.LayerCountAt(1, 1));
    }

    [Fact]
    public void Ladder_edge_traversable_both_directions()
    {
        var field = new Heightfield(4, 4);
        var grid = new HeightGrid(field);
        grid.AddLadder(1, 1, 0, 2);
        Assert.True(grid.CanStep(new TileCoord(1, 1, 0), new TileCoord(1, 1, 2)));
        Assert.True(grid.CanStep(new TileCoord(1, 1, 2), new TileCoord(1, 1, 0)));
    }

    [Fact]
    public void Ladder_partner_resolves_either_endpoint()
    {
        var field = new Heightfield(4, 4);
        var grid = new HeightGrid(field);
        grid.AddLadder(2, 2, 0, 3);
        Assert.Equal(1, grid.LadderCountAt(2, 2));
        Assert.Equal(3, grid.LadderPartnerAt(2, 2, 0, 0));
        Assert.Equal(0, grid.LadderPartnerAt(2, 2, 3, 0));
        Assert.Equal(-1, grid.LadderPartnerAt(2, 2, 1, 0));
    }

    [Fact]
    public void Add_ladder_is_idempotent()
    {
        var field = new Heightfield(4, 4);
        var grid = new HeightGrid(field);
        grid.AddLadder(1, 1, 0, 2);
        grid.AddLadder(1, 1, 0, 2);
        grid.AddLadder(1, 1, 2, 0); // reverse order
        Assert.Equal(1, grid.LadderCountAt(1, 1));
    }

    [Fact]
    public void Remove_ladder_clears_edge()
    {
        var field = new Heightfield(4, 4);
        var grid = new HeightGrid(field);
        grid.AddLadder(1, 1, 0, 2);
        grid.RemoveLadder(1, 1, 2, 0); // reverse order still removes
        Assert.Equal(0, grid.LadderCountAt(1, 1));
        Assert.False(grid.CanStep(new TileCoord(1, 1, 0), new TileCoord(1, 1, 2)));
    }

    [Fact]
    public void Same_tile_vertical_step_blocked_without_ladder()
    {
        var field = new Heightfield(4, 4);
        var grid = new HeightGrid(field);
        grid.AddWalkableLayer(1, 1, 2);
        Assert.False(grid.CanStep(new TileCoord(1, 1, 0), new TileCoord(1, 1, 2)));
    }

    [Fact]
    public void Horizontal_step_to_elevated_layer_requires_same_z()
    {
        var field = new Heightfield(4, 4);
        var grid = new HeightGrid(field);
        // Two adjacent wall tops at z=2.
        grid.MarkBlocked(1, 1, true);
        grid.AddWalkableLayer(1, 1, 2);
        grid.MarkBlocked(2, 1, true);
        grid.AddWalkableLayer(2, 1, 2);
        Assert.True(grid.CanStep(new TileCoord(1, 1, 2), new TileCoord(2, 1, 2)));
        Assert.False(grid.CanStep(new TileCoord(1, 1, 2), new TileCoord(2, 1, 0)));
    }

    [Fact]
    public void Ladder_step_cost_scales_with_layer_distance()
    {
        var field = new Heightfield(4, 4);
        var grid = new HeightGrid(field);
        grid.AddLadder(1, 1, 0, 2);
        var cost = grid.StepCost(new TileCoord(1, 1, 0), new TileCoord(1, 1, 2));
        Assert.Equal(2 * 1.4f, cost, 3);
    }
}
