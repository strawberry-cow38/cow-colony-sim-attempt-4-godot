using CowColonySim.Sim;
using CowColonySim.Sim.Designations;
using CowColonySim.Sim.Items;
using CowColonySim.Sim.Pathfinding;
using CowColonySim.Sim.Systems;
using CowColonySim.Sim.Terrain;
using CowColonySim.Sim.World;
using CowColonySim.Sim.World.Components;
using Friflo.Engine.ECS;
using Xunit;

namespace CowColonySim.Tests;

public class ConstructionJobSystemTests
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

    // Two haulers + one 5-wood blueprint + a 50-wood pile. Without
    // coordination, both haulers grab as much as they can and the bp ends
    // up with 10 deposited + a stranded carry. With telepathic coord,
    // total wood claimed (deposited + still carried) must never exceed
    // the bp's requirement.
    [Fact]
    public void Two_haulers_split_blueprint_demand_no_overdeliver()
    {
        var (world, grid, planner) = MakeWorld();

        world.SpawnColonist(0xC0F0, 4, 4);
        world.SpawnColonist(0xC0F1, 4, 5);
        world.AddOrMergeItem(5, 5, ItemKind.Wood, 50);
        var bpEntity = world.SpawnBlueprintGhost("structure.wall", 10, 10);
        var bpId = bpEntity.Id;

        var systems = new List<ITickSystem>
        {
            new ConstructionJobSystem(world, planner, grid),
            new WanderSystem(world, planner, grid),
        };

        // Run only long enough for the assignment + first pickup + first
        // deposit to settle. We don't care if the wall completes — only
        // that the in-flight wood + deposited wood never exceeds the
        // requirement at any observation point.
        var maxTicks = 60 * 30;
        var maxObserved = 0;
        for (var t = 0; t < maxTicks; t++)
        {
            TickAll(systems, t);
            if (t % 30 == 0) Thread.Sleep(20);
            var snapDep = DepositedWood(world, bpId);
            var snapInv = CountInventoryWood(world);
            var observed = snapDep + snapInv;
            if (observed > maxObserved) maxObserved = observed;
            // bp gone = build finished, can't observe further claim state.
            if (!BlueprintAlive(world, bpId)) break;
        }

        var deposited = DepositedWood(world, bpId);
        var inventoryWood = CountInventoryWood(world);
        var floorWood = CountFloorWood(world);
        var structures = CountStructureWoodEquivalent(world);
        var totalClaimed = maxObserved;

        // Conservation: anything not on floor or in-inv was either still
        // in the bp ghost OR consumed by the finished structure (5 wood).
        Assert.Equal(50, deposited + inventoryWood + floorWood + structures);

        // The actual contract: at any tick, total in-flight + deposited
        // claim never exceeded the bp's 5-wood requirement.
        Assert.True(totalClaimed <= 5,
            $"Overdelivery: peakClaimed={totalClaimed} deposited={deposited} inv={inventoryWood} floor={floorWood} structs={structures}");
    }

    // Same setup but the bp needs more than one hauler can carry — they
    // should cooperate up to but not past the requirement.
    [Fact]
    public void Two_haulers_cooperate_on_large_blueprint()
    {
        var (world, grid, planner) = MakeWorld();

        var c1 = world.SpawnColonist(0xC0F0, 4, 4);
        var c2 = world.SpawnColonist(0xC0F1, 4, 5);
        // Tiny carry caps so neither hauler can solo the build.
        ref var caps1 = ref c1.GetComponent<CarryCaps>();
        caps1.Strength = 1; caps1.BaseBulk = 2f;
        ref var caps2 = ref c2.GetComponent<CarryCaps>();
        caps2.Strength = 1; caps2.BaseBulk = 2f;

        world.AddOrMergeItem(5, 5, ItemKind.Wood, 50);
        var bpEntity = world.SpawnBlueprintGhost("workstation.stove", 10, 10); // 14 wood
        var bpId = bpEntity.Id;

        var systems = new List<ITickSystem>
        {
            new ConstructionJobSystem(world, planner, grid),
            new WanderSystem(world, planner, grid),
        };

        var maxTicks = 60 * 60 * 2;
        var peak = 0;
        for (var t = 0; t < maxTicks; t++)
        {
            TickAll(systems, t);
            if (t % 30 == 0) Thread.Sleep(20);
            var deposited = DepositedWood(world, bpId);
            var inv = CountInventoryWood(world);
            var observed = deposited + inv;
            if (observed > peak) peak = observed;
            if (!BlueprintAlive(world, bpId)) break;
            if (observed >= 14) break;
        }

        var dep = DepositedWood(world, bpId);
        var invWood = CountInventoryWood(world);
        var floor = CountFloorWood(world);
        var structs = CountStructureWoodEquivalent(world);
        Assert.Equal(50, dep + invWood + floor + structs);
        Assert.True(peak <= 14,
            $"Overdelivery: peak={peak} deposited={dep} inv={invWood} floor={floor} structs={structs}");
        Assert.True(peak >= 5,
            $"Stalled: peak={peak} deposited={dep} inv={invWood} floor={floor} structs={structs}");
    }

    private static int DepositedWood(SimWorld world, int bpId)
    {
        foreach (var e in world.Store.Query<BlueprintGhost>().Entities)
        {
            if (e.Id != bpId) continue;
            return e.GetComponent<BlueprintGhost>().MaterialDeposited;
        }
        return 0;
    }

    private static bool BlueprintAlive(SimWorld world, int bpId)
    {
        foreach (var e in world.Store.Query<BlueprintGhost>().Entities)
        {
            if (e.Id == bpId) return true;
        }
        return false;
    }

    private static int CountStructureWoodEquivalent(SimWorld world)
    {
        var sum = 0;
        foreach (var e in world.Store.Query<Structure, TilePosition>().Entities)
        {
            ref var st = ref e.GetComponent<Structure>();
            var def = CowColonySim.Sim.Blueprints.BlueprintCatalog.Get(st.DefId);
            var mats = def.MaterialsOrEmpty;
            for (var i = 0; i < mats.Count; i++)
            {
                if (mats[i].Kind == ItemKind.Wood) sum += mats[i].Count;
            }
        }
        return sum;
    }

    private static int CountFloorWood(SimWorld world)
    {
        var sum = 0;
        foreach (var e in world.Store.Query<Item, TilePosition>().Entities)
        {
            ref var it = ref e.GetComponent<Item>();
            if (it.Kind == ItemKind.Wood) sum += it.Count;
        }
        return sum;
    }

    private static int CountInventoryWood(SimWorld world)
    {
        var sum = 0;
        foreach (var e in world.Store.Query<Colonist, Inventory>().Entities)
        {
            ref var inv = ref e.GetComponent<Inventory>();
            if (inv.Stacks is null) continue;
            for (var i = 0; i < inv.Stacks.Count; i++)
            {
                var s = inv.Stacks[i];
                var def = ItemCatalog.Get(s.DefId);
                if (def.Kind == ItemKind.Wood) sum += s.Count;
            }
        }
        return sum;
    }

}
