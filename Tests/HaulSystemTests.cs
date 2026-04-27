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

    private static void TickAll(List<ITickSystem> systems, ulong tick)
    {
        var ctx = new TickContext(tick, 1.0 / 60.0, tick / 60.0);
        for (var i = 0; i < systems.Count; i++) systems[i].Tick(ctx);
    }

    [Fact]
    public async Task Auto_haul_delivers_partial_pickup_remainder_to_stockpile()
    {
        var (world, grid, planner) = MakeWorld();

        var colonist = world.SpawnColonist(0xCAFEBABE, 5, 5);
        // 5 wood at tile (10, 5). Wood = 4L bulk. BaseBulk = 10L → only 2 fit.
        var item = world.AddOrMergeItem(10, 5, ItemKind.Wood, 5);
        var stockpileRect = new TileRect(15, 5, 17, 7);
        world.SpawnZone(1, ZoneType.Stockpile, stockpileRect, TileMask.Filled(stockpileRect), "sp");

        var systems = new List<ITickSystem>
        {
            new HaulSystem(world, planner, grid),
            new WanderSystem(world, planner, grid),
        };

        // Run for up to 30s of sim time waiting for the source to fully drain.
        var maxTicks = 60 * 30;
        var ticked = 0;
        while (ticked < maxTicks)
        {
            TickAll(systems, (ulong)ticked++);
            // Path planner is async — give it a moment occasionally so results land.
            if (ticked % 30 == 0) await Task.Delay(20);
            ref var it = ref item.GetComponent<Item>();
            // Source consumed AND nothing left behind anywhere outside stockpile.
            if (it.Count == 0)
            {
                // Check no items remain outside stockpile.
                var leftOutside = false;
                foreach (var e in world.Store.Query<Item, TilePosition>().Entities)
                {
                    ref var pos = ref e.GetComponent<TilePosition>();
                    if (pos.TileX < stockpileRect.MinX || pos.TileX > stockpileRect.MaxX
                        || pos.TileY < stockpileRect.MinY || pos.TileY > stockpileRect.MaxY)
                    {
                        leftOutside = true;
                        break;
                    }
                }
                if (!leftOutside) return; // success
            }
        }

        // Diagnose
        var inv = colonist.GetComponent<Inventory>();
        ref var pos2 = ref colonist.GetComponent<TilePosition>();
        ref var work = ref colonist.GetComponent<WorkJob>();
        var totalWood = 0;
        foreach (var e in world.Store.Query<Item, TilePosition>().Entities)
        {
            ref var it = ref e.GetComponent<Item>();
            ref var ipos = ref e.GetComponent<TilePosition>();
            totalWood += it.Count;
        }
        Assert.Fail(
            $"After {maxTicks} ticks. colonist pos=({pos2.TileX},{pos2.TileY}) work.Active={work.Active} kind={work.Kind} carry={work.CarryKind} drop=({work.DropTileX},{work.DropTileY}) inv-stacks={(inv.Stacks?.Count ?? 0)} world-wood={totalWood}");
    }
}
