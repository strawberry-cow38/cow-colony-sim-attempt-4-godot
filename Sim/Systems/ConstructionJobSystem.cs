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
        // Construct is single-builder, so still single-claim per bp.
        var constructClaimed = new HashSet<int>();
        // Per-blueprint running tally of wood already accounted for —
        // existing haulers' carried + planned pickup, plus new haulers
        // assigned this tick. Lets multiple haulers cooperate on the
        // same bp without over-delivering: each one's quota is bounded
        // by `required - deposited - sum_of_others`.
        var perBpReserved = new Dictionary<int, int>();
        for (var i = 0; i < blueprints.Count; i++) perBpReserved[blueprints[i].EntityId] = 0;
        // Per-hauler max wood they're allowed to carry to the bp this
        // tick. ProgressHaul honors this when capping pickup.
        var haulerWoodQuota = new Dictionary<int, int>();
        // Blueprints already covered by an in-flight matching minified —
        // a single minified completes atomically, so further haulers
        // should never target the same bp.
        var minifiedCovered = new HashSet<int>();

        var query = _world.Store.Query<Colonist, Job, WorkJob, TilePosition, PathFollower>();
        var existingHaulers = new List<Entity>();
        foreach (var entity in query.Entities)
        {
            ref var w = ref entity.GetComponent<WorkJob>();
            if (!w.Active) continue;
            if (w.Kind == WorkKind.HaulToBlueprint)
            {
                if (w.TargetEntityId != 0) claimedItems.Add(w.TargetEntityId);
                existingHaulers.Add(entity);
            }
            else if (w.Kind == WorkKind.Construct)
            {
                constructClaimed.Add(w.TargetEntityId);
            }
        }
        // Deterministic ordering: lower entity id allocates from the
        // budget first. Two haulers same-tick on a 50-wood blueprint
        // split it predictably instead of double-counting their pickups.
        existingHaulers.Sort((a, b) => a.Id.CompareTo(b.Id));
        for (var i = 0; i < existingHaulers.Count; i++)
        {
            var entity = existingHaulers[i];
            ref var w = ref entity.GetComponent<WorkJob>();
            var bpFound = FindBlueprintAt(blueprints, w.DropTileX, w.DropTileY);
            if (!bpFound.HasValue) continue;
            var bpView = bpFound.Value;
            if (!entity.HasComponent<Inventory>() || !entity.HasComponent<CarryCaps>()) continue;
            ref var inv = ref entity.GetComponent<Inventory>();
            ref var caps = ref entity.GetComponent<CarryCaps>();

            // Minified delivery covers the full blueprint atomically.
            if (w.CarryKind == ItemKind.Minified
                || (w.TargetEntityId != 0
                    && itemsByEntity.TryGetValue(w.TargetEntityId, out var miniProbe)
                    && miniProbe.Kind == ItemKind.Minified
                    && miniProbe.MinifiedDefId == bpView.DefId))
            {
                minifiedCovered.Add(bpView.EntityId);
                haulerWoodQuota[entity.Id] = 0;
                continue;
            }

            var def = BlueprintCatalog.Get(bpView.DefId);
            var required = TotalMaterialOf(def, ItemKind.Wood);
            var taken = perBpReserved[bpView.EntityId];
            var leftover = Math.Max(0, required - bpView.MaterialDeposited - taken);
            var carried = CountInventoryOf(in inv, ItemKind.Wood);
            // Hauler is committed to deliver what they've already
            // picked up — even if leftover < carried, they still walk
            // it over and ApplyDeposit handles the spillover. Their
            // quota is what they're allowed to PICK UP MORE OF.
            var carriedShare = Math.Min(carried, leftover);
            var room = InventoryOps.RoomFor(ItemCatalog.DefaultIdFor(ItemKind.Wood), in caps, in inv);
            var pickup = 0;
            if (w.TargetEntityId != 0 && itemsByEntity.TryGetValue(w.TargetEntityId, out var src) && src.Kind == ItemKind.Wood)
            {
                pickup = Math.Max(0, Math.Min(Math.Min(src.Count, room), leftover - carriedShare));
            }
            // Quota is the absolute cap on the hauler's wood headcount —
            // ProgressHaul allows pickup up to (quota - currentCarried).
            haulerWoodQuota[entity.Id] = carried + pickup;
            perBpReserved[bpView.EntityId] = taken + carriedShare + pickup;
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
                ProgressHaul(entity, ref work, ref pf, ref pos, itemsList, itemsByEntity, blueprints, claimedItems, haulerWoodQuota);
            }
            else if (work.Active && work.Kind == WorkKind.Construct)
            {
                ProgressConstruct(entity, ref work, ref pf, ref pos);
            }
            else if (!work.Active)
            {
                if (!entity.HasComponent<Inventory>() || !entity.HasComponent<CarryCaps>()) continue;
                if (entity.HasComponent<WorkPriorities>() &&
                    entity.GetComponent<WorkPriorities>().Get(WorkType.Construction) == 0) continue;
                TryAssign(entity, ref work, ref pf, ref pos, blueprints, itemsList, itemsByEntity,
                    claimedItems, constructClaimed, perBpReserved, haulerWoodQuota, minifiedCovered);
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
                    HeightGridOps.RegisterStructure(_grid, def, pos.TileX + dx, pos.TileY + dy, g.BaseLayer);
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
        List<BlueprintSnapshot> blueprints, HashSet<int> claimedItems,
        Dictionary<int, int> haulerWoodQuota)
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
                    var bpAtDrop = FindBlueprintAt(blueprints, work.DropTileX, work.DropTileY);
                    if (bpAtDrop.HasValue) _world.UnreachableWorkTargets.Add(bpAtDrop.Value.EntityId);
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
            if (!TryChainNextPickup(entity, ref work, ref pf, ref pos, items, blueprints, claimedItems, haulerWoodQuota, in inv, in caps))
                SwitchToDropOrFinish(entity, ref work, ref pf, ref pos, ref inv);
            return;
        }
        if (pos.TileX != item.TileX || pos.TileY != item.TileY)
        {
            if (pf.LastPathFailed)
            {
                _world.UnreachableWorkTargets.Add(work.TargetEntityId);
                if (!TryChainNextPickup(entity, ref work, ref pf, ref pos, items, blueprints, claimedItems, haulerWoodQuota, in inv, in caps))
                    SwitchToDropOrFinish(entity, ref work, ref pf, ref pos, ref inv);
                return;
            }
            EnsurePath(entity, ref pf, pos.TileX, pos.TileY, item.TileX, item.TileY);
            return;
        }

        // At pickup tile — cap pickup at this hauler's per-tick quota.
        // Quota was set in pre-pass / TryAssign so multiple haulers
        // cooperate on a bp without over-delivering. Fallback to gap
        // math if no quota entry exists. Minified is atomic — never cap.
        var defId = ResolveDefId(item);
        var requested = item.Count;
        if (item.Kind != ItemKind.Minified)
        {
            if (haulerWoodQuota.TryGetValue(entity.Id, out var quota))
            {
                var carried = CountInventoryOf(in inv, item.Kind);
                requested = Math.Min(requested, Math.Max(0, quota - carried));
            }
            else
            {
                var bp = FindBlueprintAt(blueprints, work.DropTileX, work.DropTileY);
                if (bp.HasValue)
                {
                    var def = BlueprintCatalog.Get(bp.Value.DefId);
                    var required = TotalMaterialOf(def, item.Kind);
                    var gap = required - bp.Value.MaterialDeposited;
                    var carried = CountInventoryOf(in inv, item.Kind);
                    requested = Math.Min(requested, Math.Max(0, gap - carried));
                }
            }
        }
        if (requested <= 0)
        {
            SwitchToDropOrFinish(entity, ref work, ref pf, ref pos, ref inv);
            return;
        }
        var added = InventoryOps.Add(ref inv, in caps, defId, requested);
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

        if (!TryChainNextPickup(entity, ref work, ref pf, ref pos, items, blueprints, claimedItems, haulerWoodQuota, in inv, in caps))
            SwitchToDropOrFinish(entity, ref work, ref pf, ref pos, ref inv);
    }

    // Chain: another nearby item of the same CarryKind, but only while
    // the target blueprint still has a deposit gap that our current carry
    // doesn't already cover, AND only while this hauler's per-tick quota
    // has headroom (so multiple haulers cooperating on a bp don't pile on).
    private bool TryChainNextPickup(
        Entity entity, ref WorkJob work, ref PathFollower pf, ref TilePosition pos,
        List<ItemSnapshot> items, List<BlueprintSnapshot> blueprints,
        HashSet<int> claimedItems, Dictionary<int, int> haulerWoodQuota,
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
        if (haulerWoodQuota.TryGetValue(entity.Id, out var quota) && carried >= quota) return false;

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
            if (pf.LastPathFailed)
            {
                _world.UnreachableWorkTargets.Add(bpEnt.Id);
                ClearWork(ref work, ref pf);
                return;
            }
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
        Dictionary<int, ItemSnapshot> itemsByEntity,
        HashSet<int> claimedItems, HashSet<int> constructClaimed,
        Dictionary<int, int> perBpReserved, Dictionary<int, int> haulerWoodQuota,
        HashSet<int> minifiedCovered)
    {
        ref var inv = ref entity.GetComponent<Inventory>();
        ref var caps = ref entity.GetComponent<CarryCaps>();
        var carryRoom = InventoryOps.RoomFor(ItemCatalog.DefaultIdFor(ItemKind.Wood), in caps, in inv);

        var bestDist = float.PositiveInfinity;
        var bestBpId = 0;
        var bestBpX = 0;
        var bestBpY = 0;
        var bestRequired = 0;
        var bestNeedsMaterial = false;
        var bestItemId = 0;
        var bestItemX = 0;
        var bestItemY = 0;
        var bestItemKind = ItemKind.None;
        var bestProjectedPickup = 0;

        for (var bi = 0; bi < blueprints.Count; bi++)
        {
            var bp = blueprints[bi];
            if (_world.UnreachableWorkTargets.Contains(bp.EntityId)) continue;
            var def = BlueprintCatalog.Get(bp.DefId);
            if (!IsBuildable(def)) continue;
            var required = TotalMaterialOf(def, ItemKind.Wood);
            if (bp.MaterialDeposited < required)
            {
                if (minifiedCovered.Contains(bp.EntityId)) continue;
                var taken = perBpReserved.TryGetValue(bp.EntityId, out var t) ? t : 0;
                var leftover = required - bp.MaterialDeposited - taken;
                if (leftover <= 0) continue;

                var chosenItem = 0;
                var chosenX = 0;
                var chosenY = 0;
                var chosenKind = ItemKind.None;
                var chosenPickup = 0;
                // Pass 1: matching minified — atomic, only when no other
                // hauler has reserved any material to this bp yet.
                if (bp.MaterialDeposited == 0 && taken == 0)
                {
                    var bestMiniDist = float.PositiveInfinity;
                    for (var i = 0; i < items.Count; i++)
                    {
                        var it = items[i];
                        if (it.Kind != ItemKind.Minified || it.Forbidden) continue;
                        if (it.MinifiedDefId != bp.DefId) continue;
                        if (claimedItems.Contains(it.EntityId)) continue;
                        if (_world.UnreachableWorkTargets.Contains(it.EntityId)) continue;
                        var idx = it.TileX - pos.TileX;
                        var idy = it.TileY - pos.TileY;
                        var d = idx * idx + idy * idy;
                        if (d < bestMiniDist)
                        {
                            bestMiniDist = d;
                            chosenItem = it.EntityId;
                            chosenX = it.TileX;
                            chosenY = it.TileY;
                            chosenKind = ItemKind.Minified;
                            chosenPickup = it.Count;
                        }
                    }
                }
                if (chosenItem == 0)
                {
                    if (carryRoom <= 0) continue;
                    var bestItemDist = float.PositiveInfinity;
                    for (var i = 0; i < items.Count; i++)
                    {
                        var it = items[i];
                        if (it.Kind != ItemKind.Wood || it.Forbidden) continue;
                        if (claimedItems.Contains(it.EntityId)) continue;
                        if (_world.UnreachableWorkTargets.Contains(it.EntityId)) continue;
                        var idx = it.TileX - pos.TileX;
                        var idy = it.TileY - pos.TileY;
                        var d = idx * idx + idy * idy;
                        if (d < bestItemDist)
                        {
                            bestItemDist = d;
                            chosenItem = it.EntityId;
                            chosenX = it.TileX;
                            chosenY = it.TileY;
                            chosenKind = ItemKind.Wood;
                            chosenPickup = Math.Min(Math.Min(it.Count, leftover), carryRoom);
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
                    bestRequired = required;
                    bestNeedsMaterial = true;
                    bestItemId = chosenItem;
                    bestItemX = chosenX;
                    bestItemY = chosenY;
                    bestItemKind = chosenKind;
                    bestProjectedPickup = chosenPickup;
                }
            }
            else if (bp.BuildProgress < 1f)
            {
                if (constructClaimed.Contains(bp.EntityId)) continue;
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
            if (bestItemKind == ItemKind.Minified)
            {
                // Minified completes the bp atomically — reserve full
                // requirement so other haulers skip this bp entirely.
                minifiedCovered.Add(bestBpId);
                var prev = perBpReserved.TryGetValue(bestBpId, out var p) ? p : 0;
                perBpReserved[bestBpId] = prev + bestRequired;
                haulerWoodQuota[entity.Id] = 0;
            }
            else
            {
                var prev = perBpReserved.TryGetValue(bestBpId, out var p) ? p : 0;
                perBpReserved[bestBpId] = prev + bestProjectedPickup;
                haulerWoodQuota[entity.Id] = bestProjectedPickup;
            }
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
            constructClaimed.Add(bestBpId);
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
