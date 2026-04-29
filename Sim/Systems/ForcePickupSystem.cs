using CowColonySim.Sim.Designations;
using CowColonySim.Sim.Items;
using CowColonySim.Sim.Pathfinding;
using CowColonySim.Sim.World;
using CowColonySim.Sim.World.Components;
using Friflo.Engine.ECS;

namespace CowColonySim.Sim.Systems;

// Player force-pickup: walks the colonist to the item, sucks the entire
// stack into their Inventory with Locked=true, then ends the WorkJob.
// Auto-haul/auto-construct skip locked stacks, so the item sits on the
// colonist until the player force-drops it.
//
// Item deletes are deferred — Friflo crashes on archetype mutations
// inside an entity foreach.
public sealed class ForcePickupSystem : ITickSystem
{
    private readonly SimWorld _world;
    private readonly PathPlanner _planner;
    private readonly HeightGrid _grid;

    private readonly List<int> _toDelete = new();

    public ForcePickupSystem(SimWorld world, PathPlanner planner, HeightGrid grid)
    {
        _world = world;
        _planner = planner;
        _grid = grid;
    }

    public void Tick(TickContext ctx)
    {
        _toDelete.Clear();

        // Friflo Query caps at 5 type args. Inventory + CarryCaps live on
        // every colonist (added at spawn) so we can read them via HasComponent.
        var query = _world.Store.Query<Colonist, Job, WorkJob, TilePosition, PathFollower>();
        foreach (var entity in query.Entities)
        {
            if (entity.HasComponent<Drafted>() && entity.GetComponent<Drafted>().Active) continue;
            ref var job = ref entity.GetComponent<Job>();
            if (job.Active) continue;
            ref var work = ref entity.GetComponent<WorkJob>();
            if (!work.Active || work.Kind != WorkKind.ForcePickup) continue;
            if (!entity.HasComponent<Inventory>() || !entity.HasComponent<CarryCaps>()) continue;

            var item = _world.Store.GetEntityById(work.TargetEntityId);
            if (item == default || !item.HasComponent<Item>() || !item.HasComponent<TilePosition>())
            {
                ClearWork(entity, ref work);
                continue;
            }
            ref var itComp = ref item.GetComponent<Item>();
            ref var itPos = ref item.GetComponent<TilePosition>();
            if (itComp.Forbidden)
            {
                ClearWork(entity, ref work);
                continue;
            }

            ref var pos = ref entity.GetComponent<TilePosition>();
            ref var pf = ref entity.GetComponent<PathFollower>();
            if (pos.TileX != itPos.TileX || pos.TileY != itPos.TileY)
            {
                if (pf.LastPathFailed) { ClearWork(entity, ref work); continue; }
                EnsurePath(entity, ref pf, pos.TileX, pos.TileY, itPos.TileX, itPos.TileY);
                continue;
            }

            ref var inv = ref entity.GetComponent<Inventory>();
            ref var caps = ref entity.GetComponent<CarryCaps>();
            string defId;
            if (itComp.Kind == ItemKind.Minified && item.HasComponent<MinifiedThing>())
            {
                ref var mini = ref item.GetComponent<MinifiedThing>();
                defId = string.IsNullOrEmpty(mini.DefId)
                    ? ItemCatalog.DefaultIdFor(itComp.Kind) : mini.DefId;
                // Minified items aren't registered per-wrapped-def in the
                // catalog — fall back to the generic minified def for
                // weight/bulk if the wrapped DefId isn't an ItemDef.
                if (!ItemCatalog.TryGet(defId, out _)) defId = ItemCatalog.DefaultIdFor(itComp.Kind);
            }
            else
            {
                defId = ItemCatalog.DefaultIdFor(itComp.Kind);
            }
            var added = InventoryOps.AddLocked(ref inv, caps, defId, itComp.Count);
            if (added <= 0)
            {
                // No room — give up the force, leave item alone.
                ClearWork(entity, ref work);
                continue;
            }

            if (added < itComp.Count)
            {
                // Partial pickup — leave the leftover on the ground.
                itComp.Count -= added;
            }
            else
            {
                _toDelete.Add(item.Id);
            }
            ClearWork(entity, ref work);
        }

        for (var i = 0; i < _toDelete.Count; i++)
        {
            var e = _world.Store.GetEntityById(_toDelete[i]);
            if (e != default) e.DeleteEntity();
        }
    }

    private void EnsurePath(Entity entity, ref PathFollower pf, int fromX, int fromY, int toX, int toY)
    {
        if (pf.PendingRequest) return;
        if (pf.Tiles is not null && pf.Index < pf.Tiles.Length)
        {
            var last = pf.Tiles[pf.Tiles.Length - 1];
            if (last.X == toX && last.Y == toY) return;
        }
        var start = _grid.At(
            Math.Clamp(fromX, 0, _grid.Width - 1),
            Math.Clamp(fromY, 0, _grid.Height - 1));
        var goal = _grid.At(toX, toY);
        if (start == goal) { pf.Tiles = null; pf.Index = 0; return; }
        pf.Tiles = null;
        pf.Index = 0;
        pf.PendingRequest = true;
        pf.PlayerForced = true;
        pf.LastPathFailed = false;
        _planner.Request(entity.Id, start, goal);
    }

    private static void ClearWork(Entity entity, ref WorkJob work)
    {
        work.Active = false;
        work.Kind = WorkKind.None;
        work.TargetEntityId = 0;
        work.Progress = 0f;
        work.Forced = false;
        if (entity.HasComponent<PathFollower>())
        {
            ref var pf = ref entity.GetComponent<PathFollower>();
            pf.Tiles = null;
            pf.Index = 0;
        }
    }
}
