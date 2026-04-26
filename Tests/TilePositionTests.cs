using CowColonySim.Sim;
using CowColonySim.Sim.World;
using CowColonySim.Sim.World.Components;
using Xunit;

namespace CowColonySim.Tests;

public class TilePositionTests
{
    [Fact]
    public void Whole_tile_converts_to_meters()
    {
        var p = new TilePosition(2, 3, 4);
        Assert.Equal(2 * SimConstants.MetersPerTile, p.MetersX);
        Assert.Equal(3 * SimConstants.MetersPerTile, p.MetersY);
        Assert.Equal(4 * SimConstants.MetersPerTile, p.MetersZ);
    }

    [Fact]
    public void Sub_tile_offset_blends_into_meters()
    {
        var p = new TilePosition(0, 0, 0, subX: 0.5f);
        Assert.Equal(0.5f * SimConstants.MetersPerTile, p.MetersX);
    }

    [Fact]
    public void Round_trips_through_ECS()
    {
        var world = new SimWorld();
        var entity = world.CreateEntity();
        entity.AddComponent(new TilePosition(7, 8, 1, subX: 0.25f));

        var got = entity.GetComponent<TilePosition>();
        Assert.Equal(7, got.TileX);
        Assert.Equal(8, got.TileY);
        Assert.Equal(1, got.TileZ);
        Assert.Equal(0.25f, got.SubX);
    }
}
