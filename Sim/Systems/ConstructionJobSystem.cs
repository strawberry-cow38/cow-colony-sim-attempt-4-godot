using CowColonySim.Sim.Blueprints;
using CowColonySim.Sim.Designations;
using CowColonySim.Sim.Items;
using CowColonySim.Sim.Pathfinding;
using CowColonySim.Sim.World;
using CowColonySim.Sim.World.Components;
using Friflo.Engine.ECS;

namespace CowColonySim.Sim.Systems;

// Drives wall construction: idle colonists pick up wood from anywhere
// (stockpile or ground) into their Inventory, chain more of the same
// kind while inventory room and blueprint demand remain, walk to the
// hungry blueprint, drain the unlocked wood stacks, and once the
// blueprint has all its material another colonist sits on the tile and
// ticks BuildProgress to 1.0. Completion deletes the ghost, spawns a
// Structure entity, and marks the tile blocked.
//
// Inventory routing: locked + equipped stacks survive the drain, so a
// force-picked log stays put and worn gear isn't tossed.
//
// MVP scope: single material per def (Wood). One builder per blueprint
// at a time. Friflo archetype mutations are deferred outside the entity
// foreach.
public sealed class ConstructionJobSystem : ITickSystem
{
    private const float BuildPerTick = 1f / 60f;

    private readonly SimWorld _world;
    private readonly PathPlanner _planner;
    private readonly HeightGrid _grid;

    private readonly List<int> _pickupsToDelete = new();
    private readonly List<DepositAction> _deposits = new();
    private readonly List<int> _completedBlueprints = new();

    public ConstructionJobSystem(SimWorld world, PathPlanner planner, HeightGrid grid)
    {
        _world = world;
        _planner = planner;
        _grid = grid;
    }

    public void Tick(TickContext ctx)
    {
        _pickupsToDelete.Clear();
        _deposits.Clear();
        _completedBlueprints.Clear();

        var blueprints = CollectBlueprints();
        if (blueprints.Count == 0) return;

        var (itemsList, itemsByEntity) = CollectItems();

        var claimedItems = new HashSet<int>();
        var claimedBps = new HashSet<int>();
        var query = _world.Store.Query<Colonist, Job, WorkJob, TilePosition, PathFollower>();
        foreach (var entity in query.Entities)
        {
            ref var w = ref entity.GetComponent<WorkJob>();
            if (!w.Active) continue;
            if (w.Kind == WorkKind.HaulToBlueprint)
            {
                if (w.TargetEntityId != 0) claimedItems.Add(w.TargetEntityId);
                var bp = FindBlueprintAt(blueprints, w.DropTileX, w.DropTileY);
                if (bp.HasValue) claimedBps.Add(bp.Value.EntityId);
            }
            else if (w.Kind == WorkKind.Construct)
            {
                claimedBps.Add(w.TargetEntityId);
            }
        }

        foreach (var entity in query.Entities)
        {
            if (entity.HasComponent<Drafted>() && entity.GetComponent<Drafted>().Active) continue;
            ref var job = ref entity.GetComponent<Job>();
            if (job.Active) continue;
            ref var work = ref entity.GetComponent<WorkJob>();
            ref var pos = ref entity.GetComponent<TilePosition>();
            ref var pf = ref entity.GetComponent<PathFollower>();

            if (work.Active && work.Kind == WorkKind.HaulToBlueprint)
            {
                if (!entity.HasComponent<Inventory>() || !entity.HasComponent<CarryCaps>())
                {
                    ClearWork(ref work, ref pf);
                    continue;
                }
                ProgressHaul(entity, ref work, ref pf, ref pos, itemsList, itemsByEntity, blueprints, claimedItems);
            }
            else if (work.Active && work.Kind == WorkKind.Construct)
            {
                ProgressConstruct(entity, ref work, ref pf, ref pos);
            }
            else if (!work.Active)
            {
                if (!entity.HasComponent<Inventory>() || !entity.HasComponent<CarryCaps>()) continue;
                TryAssign(entity, ref work, ref pf, ref pos, blueprints, itemsList, claimedItems, claimedBps);
            }
        }

        for (var i = 0; i < _pickupsToDelete.Count; i++)
        {
            var item = _world.Store.GetEntityById(_pickupsToDelete[i]);
            if (item != default) item.DeleteEntity();
        }
        for (var i = 0; i < _deposits.Count; i++)
        {
            ApplyDeposit(_deposits[i]);
        }
        for (var i = 0; i < _completedBlueprints.Count; i++)
        {
            CompleteBlueprint(_completedBlueprints[i]);
        }
    }

    private void ApplyDeposit(DepositAction d)
    {
        var bpEnt = d.BlueprintId == 0 ? default : _world.Store.GetEntityById(d.BlueprintId);
        if (bpEnt == default)
        {
            DropPayload(d);
            return;
        }
        ref var g = ref bpEnt.GetComponent<BlueprintGhost>();
        if (d.Kind == ItemKind.Minified)
        {
            // Minified must match this blueprint's defId. Otherwise drop —
            // we accidentally hauled the wrong package.
            if (d.MinifiedDefId != g.DefId)
            {
                DropPayload(d);
                return;
            }
            var def = BlueprintCatalog.Get(g.DefId);
            g.MinifiedDelivered = true;
            g.MaterialDeposited = TotalMaterialOf(def, ItemKind.Wood);
            g.BuildProgress = 1f;
            _completedBlueprints.Add(bpEnt.Id);
            return;
        }
        var rawDef = BlueprintCatalog.Get(g.DefId);
        var required = TotalMaterialOf(rawDef, d.Kind);
        var room = required - g.MaterialDeposited;
        if (room <= 0)
        {
            _world.AddOrMergeItem(d.TileX, d.TileY, d.Kind, d.Count);
            return;
        }
        var take = Math.Min(room, d.Count);
        g.MaterialDeposited += take;
        var leftover = d.Count - take;
        if (leftover > 0) _world.AddOrMergeItem(d.TileX, d.TileY, d.Kind, leftover);
    }

    private void DropPayload(DepositAction d)
    {
        if (d.Kind == ItemKind.Minified)
        {
            _world.SpawnMinifiedThing(d.MinifiedDefId, d.TileX, d.TileY, 0, 0);
            return;
        }
        _world.AddOrMergeItem(d.TileX, d.TileY, d.Kind, d.Count);
    }

    private void CompleteBlueprint(int bpId)
    {
        var bpEnt = _world.Store.GetEntityById(bpId);
        if (bpEnt == default) return;
        ref var g = ref bpEnt.GetComponent<BlueprintGhost>();
        ref var pos = ref bpEnt.GetComponent<TilePosition>();
        var def = BlueprintCatalog.Get(g.DefId);
        _world.SpawnStructure(g.DefId, pos.TileX, pos.TileY, g.Rotation, g.BaseLayer);
        if (def.Category == BlueprintCategory.Structure)
        {
            var (footW, footH) = RotatedFootprint(def.FootprintW, def.FootprintH, g.Rotation);
            for (var dy = 0; dy < footH; dy++)
            {
                for (var dx = 0; dx < footW; dx++)
                {
                    _grid.MarkBlocked(pos.TileX + dx, pos.TileY + dy, true);
                }
            }
        }
        bpEnt.DeleteEntity();
    }

    private static (int w, int h) RotatedFootprint(int w, int h, int rot)
        => (rot & 1) == 0 ? (w, h) : (h, w);

    private void ProgressHaul(
        Entity entity, ref WorkJob work, ref PathFollower pf, ref TilePosition pos,
        List<ItemSnapshot> items, Dictionary<int, ItemSnapshot> itemsByEntity,
        List<BlueprintSnapshot> blueprints, HashSet<int> claimedItems)
    {
        ref var inv = ref entity.GetComponent<Inventory>();
        ref var caps = ref entity.GetComponent<CarryCaps>();

        // Drop phase — TargetEntityId == 0 means pickup chain ended.
        if (work.TargetEntityId == 0)
        {
            if (pos.TileX != work.DropTileX || pos.TileY != work.DropTileY)
            {
                if (pf.LastPathFailed)
                {
                    DrainCarriedToTile(ref inv, pos.TileX, pos.TileY, work.CarryKind);
                    ClearWork(ref work, ref pf);
                    return;
                }
                EnsurePath(entity, ref pf, pos.TileX, pos.TileY, work.DropTileX, work.DropTileY);
                return;
            }
            DrainCarriedToBlueprint(ref inv, work.DropTileX, work.DropTileY, work.CarryKind, blueprints);
            ClearWork(ref work, ref pf);
            return;
        }

        // Pickup phase
        if (!itemsByEntity.TryGetValue(work.TargetEntityId, out var item) || item.Forbidden)
        {
            if (!TryChainNextPickup(entity, ref work, ref pf, ref pos, items, blueprints, claimedItems, in inv, in caps))
                SwitchToDropOrFinish(entity, ref work, ref pf, ref pos, ref inv);
            return;
        }
        if (pos.TileX != item.TileX || pos.TileY != item.TileY)
        {
            if (pf.LastPathFailed)
            {
                if (!TryChainNextPickup(entity, ref work, ref pf, ref pos, items, blueprints, claimedItems, in inv, in caps))
                    SwitchToDropOrFinish(entity, ref work, ref pf, ref pos, ref inv);
                return;
            }
            EnsurePath(entity, ref pf, pos.TileX, pos.TileY, item.TileX, item.TileY);
            return;
        }

        // At pickup tile — pull as much of the stack as fits into inventory.
        var defId = ResolveDefId(item);
        var added = InventoryOps.Add(ref inv, in caps, defId, item.Count);
        if (added <= 0)
        {
            SwitchToDropOrFinish(entity, ref work, ref pf, ref pos, ref inv);
            return;
        }
        if (work.CarryKind == ItemKind.None) work.CarryKind = item.Kind;

        if (added < item.Count)
        {
            var src = _world.Store.GetEntityById(work.TargetEntityId);
            if (src != default && src.HasComponent<Item>())
            {
                ref var srcIt = ref src.GetComponent<Item>();
                srcIt.Count = Math.Max(0, srcIt.Count - added);
                if (srcIt.Count == 0) _pickupsToDelete.Add(work.TargetEntityId);
            }
            claimedItems.Add(work.TargetEntityId);
            SwitchToDropOrFinish(entity, ref work, ref pf, ref pos, ref inv);
            return;
        }

        _pickupsToDelete.Add(work.TargetEntityId);
        claimedItems.Add(work.TargetEntityId);

        // Minified completes the blueprint atomically — never chain.
        if (item.Kind == ItemKind.Minified)
        {
            SwitchToDropOrFinish(entity, ref work, ref pf, ref pos, ref inv);
            return;
        }

        if (!TryChainNextPickup(entity, ref work, ref pf, ref pos, items, blueprints, claimedItems, in inv, in caps))
            SwitchToDropOrFinish(entity, ref work, ref pf, ref pos, ref inv);
    }

    // Chain: another nearby item of the same CarryKind, but only while
    // the target blueprint still has a deposit gap that our current carry
    // doesn't already cover. Stops a hauler from over-filling a 5-wood
    // wall when they could service a different blueprint.
    private bool TryChainNextPickup(
        Entity entity, ref WorkJob work, ref PathFollower pf, ref TilePosition pos,
        List<ItemSnapshot> items, List<BlueprintSnapshot> blueprints,
        HashSet<int> claimedItems,
        in Inventory inv, in CarryCaps caps)
    {
        if (work.CarryKind == ItemKind.None || work.CarryKind == ItemKind.Minified) return false;
        var bp = FindBlueprintAt(blueprints, work.DropTileX, work.DropTileY);
        if (!bp.HasValue) return false;
        var def = BlueprintCatalog.Get(bp.Value.DefId);
        var required = TotalMaterialOf(def, work.CarryKind);
        var gap = required - bp.Value.MaterialDeposited;
        if (gap <= 0) return false;
        var carried = CountInventoryOf(in inv, work.CarryKind);
        if (carried >= gap) return false;

        var room = InventoryOps.RoomFor(ItemCatalog.DefaultIdFor(work.CarryKind), in caps, in inv);
        if (room <= 0) return false;

        var bestId = 0;
        var bestX = 0;
        var bestY = 0;
        var bestDist = float.PositiveInfinity;
        for (var i = 0; i < items.Count; i++)
        {
            var it = items[i];
            if (it.Kind != work.CarryKind || it.Forbidden) continue;
            if (claimedItems.Contains(it.EntityId)) continue;
            var dx = it.TileX - pos.TileX;
            var dy = it.TileY - pos.TileY;
            var d = dx * dx + dy * dy;
            if (d < bestDist)
            {
                bestDist = d;
                bestId = it.EntityId;
                bestX = it.TileX;
                bestY = it.TileY;
            }
        }
        if (bestId == 0) return false;

        work.TargetEntityId = bestId;
        work.TargetTileX = bestX;
        work.TargetTileY = bestY;
        claimedItems.Add(bestId);
        EnsurePath(entity, ref pf, pos.TileX, pos.TileY, bestX, bestY);
        return true;
    }

    private void SwitchToDropOrFinish(
        Entity entity, ref WorkJob work, ref PathFollower pf, ref TilePosition pos,
        ref Inventory inv)
    {
        var holds = false;
        if (inv.Stacks is not null)
        {
            for (var i = 0; i < inv.Stacks.Count; i++)
            {
                var s = inv.Stacks[i];
                if (s.Locked || s.Equipped) continue;
                var def = ItemCatalog.Get(s.DefId);
                if (def.Kind != work.CarryKind) continue;
                holds = true;
                break;
            }
        }
        if (!holds)
        {
            ClearWork(ref work, ref pf);
            return;
        }
        work.TargetEntityId = 0;
        EnsurePath(entity, ref pf, pos.TileX, pos.TileY, work.DropTileX, work.DropTileY);
    }

    private void DrainCarriedToBlueprint(
        ref Inventory inv, int tileX, int tileY, ItemKind kind, List<BlueprintSnapshot> blueprints)
    {
        if (inv.Stacks is null || kind == ItemKind.None) return;
        var bp = FindBlueprintAt(blueprints, tileX, tileY);
        var bpId = bp.HasValue ? bp.Value.EntityId : 0;
        for (var i = inv.Stacks.Count - 1; i >= 0; i--)
        {
            var s = inv.Stacks[i];
            if (s.Locked || s.Equipped) continue;
            var def = ItemCatalog.Get(s.DefId);
            if (def.Kind != kind) continue;
            if (def.Kind == ItemKind.Minified)
                _deposits.Add(new DepositAction(bpId, def.Kind, s.Count, tileX, tileY, s.DefId));
            else
                _deposits.Add(new DepositAction(bpId, def.Kind, s.Count, tileX, tileY, string.Empty));
            inv.Stacks.RemoveAt(i);
        }
    }

    private void DrainCarriedToTile(ref Inventory inv, int tileX, int tileY, ItemKind kind)
    {
        if (inv.Stacks is null || kind == ItemKind.None) return;
        for (var i = inv.Stacks.Count - 1; i >= 0; i--)
        {
            var s = inv.Stacks[i];
            if (s.Locked || s.Equipped) continue;
            var def = ItemCatalog.Get(s.DefId);
            if (def.Kind != kind) continue;
            if (def.Kind == ItemKind.Minified)
                _deposits.Add(new DepositAction(0, def.Kind, s.Count, tileX, tileY, s.DefId));
            else
                _deposits.Add(new DepositAction(0, def.Kind, s.Count, tileX, tileY, string.Empty));
            inv.Stacks.RemoveAt(i);
        }
    }

    private static int CountInventoryOf(in Inventory inv, ItemKind kind)
    {
        if (inv.Stacks is null) return 0;
        var sum = 0;
        for (var i = 0; i < inv.Stacks.Count; i++)
        {
            var s = inv.Stacks[i];
            if (s.Locked || s.Equipped) continue;
            var def = ItemCatalog.Get(s.DefId);
            if (def.Kind != kind) continue;
            sum += s.Count;
        }
        return sum;
    }

    private static string ResolveDefId(ItemSnapshot item)
    {
        if (item.Kind == ItemKind.Minified)
        {
            return ItemCatalog.TryGet(item.MinifiedDefId, out _)
                ? item.MinifiedDefId : ItemCatalog.DefaultIdFor(item.Kind);
        }
        return ItemCatalog.DefaultIdFor(item.Kind);
    }

    private void ProgressConstruct(
        Entity entity, ref WorkJob work, ref PathFollower pf, ref TilePosition pos)
    {
        var bpEnt = _world.Store.GetEntityById(work.TargetEntityId);
        if (bpEnt == default || !bpEnt.HasComponent<BlueprintGhost>())
        {
            ClearWork(ref work, ref pf);
            return;
        }
        ref var g = ref bpEnt.GetComponent<BlueprintGhost>();
        var def = BlueprintCatalog.Get(g.DefId);
        var required = TotalMaterialOf(def, ItemKind.Wood);
        if (g.MaterialDeposited < required)
        {
            ClearWork(ref work, ref pf);
            return;
        }

        if (pos.TileX != work.TargetTileX || pos.TileY != work.TargetTileY)
        {
            if (pf.LastPathFailed) { ClearWork(ref work, ref pf); return; }
            EnsurePath(entity, ref pf, pos.TileX, pos.TileY, work.TargetTileX, work.TargetTileY);
            return;
        }

        g.BuildProgress = MathF.Min(1f, g.BuildProgress + BuildPerTick);
        if (g.BuildProgress >= 1f)
        {
            _completedBlueprints.Add(bpEnt.Id);
            ClearWork(ref work, ref pf);
        }
    }

    private void TryAssign(
        Entity entity, ref WorkJob work, ref PathFollower pf, ref TilePosition pos,
        List<BlueprintSnapshot> blueprints, List<ItemSnapshot> items,
        HashSet<int> claimedItems, HashSet<int> claimedBps)
    {
        var bestDist = float.PositiveInfinity;
        var bestBpId = 0;
        var bestBpX = 0;
        var bestBpY = 0;
        var bestNeedsMaterial = false;
        var bestItemId = 0;
        var bestItemX = 0;
        var bestItemY = 0;

        for (var bi = 0; bi < blueprints.Count; bi++)
        {
            var bp = blueprints[bi];
            if (claimedBps.Contains(bp.EntityId)) continue;
            var def = BlueprintCatalog.Get(bp.DefId);
            if (!IsBuildable(def)) continue;
            var required = TotalMaterialOf(def, ItemKind.Wood);
            if (bp.MaterialDeposited < required)
            {
                var chosenItem = 0;
                var chosenX = 0;
                var chosenY = 0;
                // Pass 1: matching minified package wins outright — drops one
                // payload and the blueprint completes, no wood needed.
                if (bp.MaterialDeposited == 0)
                {
                    var bestMiniDist = float.PositiveInfinity;
                    for (var i = 0; i < items.Count; i++)
                    {
                        var it = items[i];
                        if (it.Kind != ItemKind.Minified || it.Forbidden) continue;
                        if (it.MinifiedDefId != bp.DefId) continue;
                        if (claimedItems.Contains(it.EntityId)) continue;
                        var idx = it.TileX - pos.TileX;
                        var idy = it.TileY - pos.TileY;
                        var d = idx * idx + idy * idy;
                        if (d < bestMiniDist)
                        {
                            bestMiniDist = d;
                            chosenItem = it.EntityId;
                            chosenX = it.TileX;
                            chosenY = it.TileY;
                        }
                    }
                }
                if (chosenItem == 0)
                {
                    var bestItemDist = float.PositiveInfinity;
                    for (var i = 0; i < items.Count; i++)
                    {
                        var it = items[i];
                        if (it.Kind != ItemKind.Wood || it.Forbidden) continue;
                        if (claimedItems.Contains(it.EntityId)) continue;
                        var idx = it.TileX - pos.TileX;
                        var idy = it.TileY - pos.TileY;
                        var d = idx * idx + idy * idy;
                        if (d < bestItemDist)
                        {
                            bestItemDist = d;
                            chosenItem = it.EntityId;
                            chosenX = it.TileX;
                            chosenY = it.TileY;
                        }
                    }
                }
                if (chosenItem == 0) continue;
                var bpDx = bp.TileX - pos.TileX;
                var bpDy = bp.TileY - pos.TileY;
                var bpDist = bpDx * bpDx + bpDy * bpDy;
                if (bpDist < bestDist)
                {
                    bestDist = bpDist;
                    bestBpId = bp.EntityId;
                    bestBpX = bp.TileX;
                    bestBpY = bp.TileY;
                    bestNeedsMaterial = true;
                    bestItemId = chosenItem;
                    bestItemX = chosenX;
                    bestItemY = chosenY;
                }
            }
            else if (bp.BuildProgress < 1f)
            {
                var bpDx = bp.TileX - pos.TileX;
                var bpDy = bp.TileY - pos.TileY;
                var bpDist = bpDx * bpDx + bpDy * bpDy;
                if (bpDist < bestDist)
                {
                    bestDist = bpDist;
                    bestBpId = bp.EntityId;
                    bestBpX = bp.TileX;
                    bestBpY = bp.TileY;
                    bestNeedsMaterial = false;
                }
            }
        }

        if (bestBpId == 0) return;

        if (bestNeedsMaterial)
        {
            work.Active = true;
            work.Kind = WorkKind.HaulToBlueprint;
            work.TargetEntityId = bestItemId;
            work.TargetTileX = bestItemX;
            work.TargetTileY = bestItemY;
            work.DropTileX = bestBpX;
            work.DropTileY = bestBpY;
            work.Progress = 0f;
            work.Forced = false;
            work.Carrying = false;
            work.CarryKind = ItemKind.None;
            work.CarryCount = 0;
            work.CarryMinifiedDefId = null;
            claimedItems.Add(bestItemId);
            claimedBps.Add(bestBpId);
            EnsurePath(entity, ref pf, pos.TileX, pos.TileY, bestItemX, bestItemY);
        }
        else
        {
            work.Active = true;
            work.Kind = WorkKind.Construct;
            work.TargetEntityId = bestBpId;
            work.TargetTileX = bestBpX;
            work.TargetTileY = bestBpY;
            work.DropTileX = 0;
            work.DropTileY = 0;
            work.Progress = 0f;
            work.Forced = false;
            work.Carrying = false;
            work.CarryKind = ItemKind.None;
            work.CarryCount = 0;
            claimedBps.Add(bestBpId);
            EnsurePath(entity, ref pf, pos.TileX, pos.TileY, bestBpX, bestBpY);
        }
    }

    private static bool IsBuildable(BlueprintDef def) =>
        def.MaterialsOrEmpty.Count > 0;

    private static int TotalMaterialOf(BlueprintDef def, ItemKind kind)
    {
        var sum = 0;
        var list = def.MaterialsOrEmpty;
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i].Kind == kind) sum += list[i].Count;
        }
        return sum;
    }

    private static BlueprintSnapshot? FindBlueprintAt(List<BlueprintSnapshot> bps, int x, int y)
    {
        for (var i = 0; i < bps.Count; i++)
        {
            var bp = bps[i];
            if (bp.TileX == x && bp.TileY == y) return bp;
        }
        return null;
    }

    private List<BlueprintSnapshot> CollectBlueprints()
    {
        var list = new List<BlueprintSnapshot>();
        foreach (var entity in _world.Store.Query<BlueprintGhost, TilePosition>().Entities)
        {
            ref var g = ref entity.GetComponent<BlueprintGhost>();
            ref var p = ref entity.GetComponent<TilePosition>();
            list.Add(new BlueprintSnapshot(entity.Id, g.DefId, p.TileX, p.TileY, g.MaterialDeposited, g.BuildProgress));
        }
        return list;
    }

    private (List<ItemSnapshot>, Dictionary<int, ItemSnapshot>) CollectItems()
    {
        var list = new List<ItemSnapshot>();
        var byEntity = new Dictionary<int, ItemSnapshot>();
        foreach (var entity in _world.Store.Query<Item, TilePosition>().Entities)
        {
            ref var it = ref entity.GetComponent<Item>();
            ref var pos = ref entity.GetComponent<TilePosition>();
            var miniDef = string.Empty;
            if (entity.HasComponent<MinifiedThing>())
            {
                miniDef = entity.GetComponent<MinifiedThing>().DefId;
            }
            var snap = new ItemSnapshot(entity.Id, it.Kind, it.Count, pos.TileX, pos.TileY, it.Forbidden, miniDef);
            list.Add(snap);
            byEntity[entity.Id] = snap;
        }
        return (list, byEntity);
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
        pf.PlayerForced = false;
        pf.LastPathFailed = false;
        _planner.Request(entity.Id, start, goal);
    }

    private static void ClearWork(ref WorkJob work, ref PathFollower pf)
    {
        work.Active = false;
        work.Kind = WorkKind.None;
        work.TargetEntityId = 0;
        work.Progress = 0f;
        work.Forced = false;
        work.Carrying = false;
        work.CarryKind = ItemKind.None;
        work.CarryCount = 0;
        work.CarryMinifiedDefId = null;
        pf.Tiles = null;
        pf.Index = 0;
    }

    private readonly struct BlueprintSnapshot
    {
        public readonly int EntityId;
        public readonly string DefId;
        public readonly int TileX;
        public readonly int TileY;
        public readonly int MaterialDeposited;
        public readonly float BuildProgress;
        public BlueprintSnapshot(int id, string defId, int tx, int ty, int deposited, float progress)
        {
            EntityId = id; DefId = defId; TileX = tx; TileY = ty;
            MaterialDeposited = deposited; BuildProgress = progress;
        }
    }

    private readonly struct ItemSnapshot
    {
        public readonly int EntityId;
        public readonly ItemKind Kind;
        public readonly int Count;
        public readonly int TileX;
        public readonly int TileY;
        public readonly bool Forbidden;
        public readonly string MinifiedDefId;
        public ItemSnapshot(int id, ItemKind k, int c, int tx, int ty, bool forbidden, string miniDef)
        {
            EntityId = id; Kind = k; Count = c; TileX = tx; TileY = ty; Forbidden = forbidden;
            MinifiedDefId = miniDef;
        }
    }

    private readonly struct DepositAction
    {
        public readonly int BlueprintId;
        public readonly ItemKind Kind;
        public readonly int Count;
        public readonly int TileX;
        public readonly int TileY;
        public readonly string MinifiedDefId;
        public DepositAction(int bp, ItemKind k, int c, int tx, int ty, string miniDef)
        { BlueprintId = bp; Kind = k; Count = c; TileX = tx; TileY = ty; MinifiedDefId = miniDef; }
    }
}
