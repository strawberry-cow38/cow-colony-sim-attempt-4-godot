using CowColonySim.Sim;
using CowColonySim.Sim.Items;
using CowColonySim.Sim.Pathfinding;
using CowColonySim.Sim.Systems;
using CowColonySim.Sim.Terrain;
using CowColonySim.Sim.World;
using CowColonySim.Sim.World.Components;
using CowColonySim.Sim.Zones;
using Xunit;

namespace CowColonySim.Tests;

public class HaulSystemTests
{
    private static (SimWorld world, HeightGrid grid, PathPlanner planner) MakeWorld()
    {
        var field = new Heightfield(32, 32);
        var grid = new HeightGrid(field);
        var planner = new PathPlanner(grid, maxConcurrency: 1);
        var world = new SimWorld();
        return (world, grid, planner);
    }

    private static void TickAll(List<ITickSystem> systems, long tick)
    {
        var ctx = new TickContext(tick, 1.0 / 60.0);
        for (var i = 0; i < systems.Count; i++) systems[i].Tick(ctx);
    }

    [Fact]
    public void Auto_haul_delivers_partial_pickup_remainder_to_stockpile()
    {
        var (world, grid, planner) = MakeWorld();

        var colonist = world.SpawnColonist(0xCAFEBABE, 4, 4);
        // 5 wood at tile (5, 4). Wood = 4L bulk. BaseBulk = 10L → only 2 fit per trip.
        var item = world.AddOrMergeItem(5, 4, ItemKind.Wood, 5);
        var stockpileRect = new TileRect(6, 4, 7, 5);
        world.SpawnZone(1, ZoneType.Stockpile, stockpileRect, TileMask.Filled(stockpileRect), "sp");

        var systems = new List<ITickSystem>
        {
            new HaulSystem(world, planner, grid),
            new WanderSystem(world, planner, grid),
        };

        var maxTicks = 60 * 30;
        var ticked = 0;
        while (ticked < maxTicks)
        {
            TickAll(systems, ticked++);
            // Path planner is async — give it a moment occasionally so results land.
            if (ticked % 30 == 0) Thread.Sleep(20);

            if (IsDone(world, item.Id, stockpileRect)) return; // success
        }

        // Diagnose
        ref var inv = ref colonist.GetComponent<Inventory>();
        ref var pos2 = ref colonist.GetComponent<TilePosition>();
        ref var work = ref colonist.GetComponent<WorkJob>();
        ref var pf = ref colonist.GetComponent<PathFollower>();
        var totalWood = 0;
        foreach (var e in world.Store.Query<Item, TilePosition>().Entities)
        {
            ref var it = ref e.GetComponent<Item>();
            totalWood += it.Count;
        }
        var pathDesc = pf.Tiles is null
            ? "null"
            : $"len={pf.Tiles.Length} idx={pf.Index} last=({pf.Tiles[pf.Tiles.Length - 1].X},{pf.Tiles[pf.Tiles.Length - 1].Y})";
        Assert.Fail(
            $"After {maxTicks} ticks. colonist pos=({pos2.TileX},{pos2.TileY}) sub=({pos2.SubX:F2},{pos2.SubY:F2}) work.Active={work.Active} kind={work.Kind} carry={work.CarryKind} drop=({work.DropTileX},{work.DropTileY}) inv-stacks={(inv.Stacks?.Count ?? 0)} world-wood={totalWood} pf.pending={pf.PendingRequest} pf.failed={pf.LastPathFailed} pf.tiles=[{pathDesc}]");
    }

    private static bool IsDone(SimWorld world, int sourceId, TileRect stockpileRect)
    {
        var sourceAlive = false;
        foreach (var e in world.Store.Query<Item, TilePosition>().Entities)
        {
            ref var it = ref e.GetComponent<Item>();
            ref var pos = ref e.GetComponent<TilePosition>();
            if (e.Id == sourceId && it.Count != 0) sourceAlive = true;
            if (pos.TileX < stockpileRect.MinX || pos.TileX > stockpileRect.MaxX
                || pos.TileY < stockpileRect.MinY || pos.TileY > stockpileRect.MaxY)
            {
                return false;
            }
        }
        return !sourceAlive;
    }
}
