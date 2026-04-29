using CowColonySim.Sim.Blueprints;
using CowColonySim.Sim.Commands;
using CowColonySim.Sim.Designations;
using CowColonySim.Sim.Items;
using CowColonySim.Sim.Pathfinding;
using CowColonySim.Sim.World;
using CowColonySim.Sim.World.Components;
using CowColonySim.Sim.Zones;
using Friflo.Engine.ECS;

namespace CowColonySim.Sim.Systems;

// Dispatches queued commands at the top of each tick. Currently only
// MoveCommand: validates the entity, fires an A* request, clears the
// existing path so the colonist commits to the new destination as soon
// as the result lands.
public sealed class CommandSystem : ITickSystem
{
    private readonly CommandBus _bus;
    private readonly SimWorld _world;
    private readonly PathPlanner _planner;
    private readonly HeightGrid _grid;

    public CommandSystem(CommandBus bus, SimWorld world, PathPlanner planner, HeightGrid grid)
    {
        _bus = bus;
        _world = world;
        _planner = planner;
        _grid = grid;
    }

    public void Tick(TickContext ctx)
    {
        while (_bus.TryDequeue(out var command))
        {
            switch (command)
            {
                case MoveCommand move:
                    Apply(move);
                    break;
                case InvalidatePathsInRegion region:
                    Apply(region);
                    break;
                case CreateZoneCommand cz:
                    Apply(cz);
                    break;
                case StampDesignationsCommand sd:
                    Apply(sd);
                    break;
                case PlaceBlueprintGhostCommand pb:
                    Apply(pb);
                    break;
                case EraseInRectCommand er:
                    Apply(er);
                    break;
                case SetZoneSettingsCommand szs:
                    Apply(szs);
                    break;
                case PrioritizeChopCommand pc:
                    Apply(pc);
                    break;
                case PrioritizeMineCommand pm:
                    Apply(pm);
                    break;
                case PrioritizeBuildCommand pb:
                    Apply(pb);
                    break;
                case PrioritizeHaulCommand ph:
                    Apply(ph);
                    break;
                case SetItemForbiddenCommand sf:
                    Apply(sf);
                    break;
                case CancelBlueprintCommand cb:
                    Apply(cb);
                    break;
                case UninstallStructureCommand us:
                    Apply(us);
                    break;
                case DeconstructStructureCommand ds:
                    ApplyDeconstruct(ds.EntityId);
                    break;
                case ForcePickupCommand fp:
                    Apply(fp);
                    break;
                case ForceDropFromInventoryCommand fd:
                    Apply(fd);
                    break;
                case EquipFromInventoryCommand eq:
                    Apply(eq);
                    break;
                case UnequipInventoryCommand uq:
                    Apply(uq);
                    break;
                case SetDraftedCommand sdr:
                    Apply(sdr);
                    break;
                case SetWorkPriorityCommand swp:
                    Apply(swp);
                    break;
                case SetGeneratorOutputCommand sgo:
                    Apply(sgo);
                    break;
            }
        }
    }

    private void Apply(SetWorkPriorityCommand cmd)
    {
        var entity = _world.Store.GetEntityById(cmd.ColonistId);
        if (entity == default || !entity.HasComponent<WorkPriorities>()) return;
        ref var prios = ref entity.GetComponent<WorkPriorities>();
        prios.Set(cmd.WorkType, cmd.Priority);
    }

    private void Apply(SetGeneratorOutputCommand cmd)
    {
        var entity = _world.Store.GetEntityById(cmd.EntityId);
        if (entity == default || !entity.HasComponent<PowerNode>() || !entity.HasComponent<Structure>()) return;
        ref var node = ref entity.GetComponent<PowerNode>();
        if (node.Kind != PowerNodeKind.Source) return;
        ref var s = ref entity.GetComponent<Structure>();
        var max = BlueprintCatalog.TryGet(s.DefId, out var def) && def is not null ? def.MaxSupplyW : float.MaxValue;
        node.SupplyW = Math.Clamp(cmd.Watts, 0f, max > 0f ? max : cmd.Watts);
        node.IsActive = cmd.IsOn;
        _world.BumpPowerVersion();
    }

    private void Apply(SetDraftedCommand cmd)
    {
        if (cmd.EntityIds is null) return;
        for (var i = 0; i < cmd.EntityIds.Count; i++)
        {
            var entity = _world.Store.GetEntityById(cmd.EntityIds[i]);
            if (entity == default || !entity.HasComponent<Drafted>()) continue;
            ref var d = ref entity.GetComponent<Drafted>();
            d.Active = cmd.Drafted;
            // Drafting yanks colonists out of any auto-job in flight so
            // they actually stand still on the spot. Undrafting just
            // releases — the next tick of JobSystem/HaulSystem picks
            // them back up.
            if (cmd.Drafted)
            {
                if (entity.HasComponent<Job>())
                {
                    ref var j = ref entity.GetComponent<Job>();
                    j.Active = false;
                }
                if (entity.HasComponent<WorkJob>())
                {
                    ref var w = ref entity.GetComponent<WorkJob>();
                    if (w.Active && w.Kind == WorkKind.HaulItem)
                        DrainUnlockedToTile(entity, w.CarryKind);
                    w.Active = false;
                    w.Kind = WorkKind.None;
                    w.TargetEntityId = 0;
                    w.Forced = false;
                }
                if (entity.HasComponent<PathFollower>())
                {
                    ref var pf = ref entity.GetComponent<PathFollower>();
                    pf.Tiles = null;
                    pf.Index = 0;
                    pf.PlayerForced = false;
                }
            }
        }
    }

    private void Apply(ForcePickupCommand cmd)
    {
        var colonist = _world.Store.GetEntityById(cmd.ColonistId);
        if (colonist == default) return;
        if (!colonist.HasComponent<WorkJob>() || !colonist.HasComponent<PathFollower>()
            || !colonist.HasComponent<TilePosition>()) return;

        var item = _world.Store.GetEntityById(cmd.ItemEntityId);
        if (item == default || !item.HasComponent<Item>() || !item.HasComponent<TilePosition>()) return;
        ref var itComp = ref item.GetComponent<Item>();
        if (itComp.Forbidden) return;
        ref var itPos = ref item.GetComponent<TilePosition>();

        // Clear any other haul targeting this stack — the lock-pickup
        // shape mirrors PrioritizeHaul's pre-emption logic.
        foreach (var other in _world.Store.Query<Colonist, WorkJob, PathFollower, TilePosition>().Entities)
        {
            if (other.Id == cmd.ColonistId) continue;
            ref var ow = ref other.GetComponent<WorkJob>();
            if (!ow.Active || ow.TargetEntityId != cmd.ItemEntityId) continue;
            ref var opf = ref other.GetComponent<PathFollower>();
            DrainUnlockedToTile(other, ow.CarryKind);
            ResetWorkJob(ref ow, ref opf);
        }

        ref var work = ref colonist.GetComponent<WorkJob>();
        ref var pf = ref colonist.GetComponent<PathFollower>();
        work.Active = true;
        work.Kind = WorkKind.ForcePickup;
        work.TargetEntityId = cmd.ItemEntityId;
        work.TargetTileX = itPos.TileX;
        work.TargetTileY = itPos.TileY;
        work.DropTileX = 0;
        work.DropTileY = 0;
        work.Progress = 0f;
        work.Forced = true;
        work.Carrying = false;
        work.CarryKind = ItemKind.None;
        work.CarryCount = 0;
        work.CarryMinifiedDefId = null;
        pf.Tiles = null;
        pf.Index = 0;
    }

    private void Apply(ForceDropFromInventoryCommand cmd)
    {
        var colonist = _world.Store.GetEntityById(cmd.ColonistId);
        if (colonist == default) return;
        if (!colonist.HasComponent<Inventory>() || !colonist.HasComponent<TilePosition>()) return;
        ref var inv = ref colonist.GetComponent<Inventory>();
        ref var pos = ref colonist.GetComponent<TilePosition>();
        var (defId, count, wrappedDefId) = InventoryOps.RemoveAt(ref inv, cmd.StackIndex);
        if (count <= 0 || string.IsNullOrEmpty(defId)) return;
        var def = ItemCatalog.Get(defId);
        if (def.Kind == ItemKind.Minified)
        {
            // Wrapped blueprint id rides on the inv stack so we can
            // recreate the right structure here. Rotation/BaseLayer
            // aren't persisted in the stack yet — Phase-3 follow-up.
            if (string.IsNullOrEmpty(wrappedDefId)) return;
            _world.SpawnMinifiedThing(wrappedDefId, pos.TileX, pos.TileY, 0, 0);
        }
        else
        {
            _world.AddOrMergeItem(pos.TileX, pos.TileY, def.Kind, count);
        }
    }

    private void Apply(EquipFromInventoryCommand cmd)
    {
        var colonist = _world.Store.GetEntityById(cmd.ColonistId);
        if (colonist == default || !colonist.HasComponent<Inventory>()) return;
        ref var inv = ref colonist.GetComponent<Inventory>();
        InventoryOps.Equip(ref inv, cmd.StackIndex);
    }

    private void Apply(UnequipInventoryCommand cmd)
    {
        var colonist = _world.Store.GetEntityById(cmd.ColonistId);
        if (colonist == default || !colonist.HasComponent<Inventory>()) return;
        ref var inv = ref colonist.GetComponent<Inventory>();
        InventoryOps.Unequip(ref inv, cmd.StackIndex);
    }

    // Toggle a structure-work designation on the structure's tile. If a
    // matching designation already exists, this cancels it (clears any
    // matching active WorkJob too). Otherwise stamps a new designation
    // for StructureWorkSystem to pick up.
    private void Apply(UninstallStructureCommand cmd) =>
        ToggleStructureDesignation(cmd.EntityId, DesignationKind.Uninstall, WorkKind.Uninstall);

    private void ApplyDeconstruct(int structureId) =>
        ToggleStructureDesignation(structureId, DesignationKind.Deconstruct, WorkKind.Deconstruct);

    private void ToggleStructureDesignation(int structureId, DesignationKind dKind, WorkKind wKind)
    {
        var ent = _world.Store.GetEntityById(structureId);
        if (ent == default || !ent.HasComponent<Structure>() || !ent.HasComponent<TilePosition>()) return;
        ref var pos = ref ent.GetComponent<TilePosition>();
        var tx = pos.TileX;
        var ty = pos.TileY;

        // Cancel branch: nuke an existing matching designation + any
        // colonist actively working on it.
        foreach (var d in _world.Store.Query<Designation, TilePosition>().Entities)
        {
            ref var dc = ref d.GetComponent<Designation>();
            if (dc.Kind != dKind) continue;
            ref var dp = ref d.GetComponent<TilePosition>();
            if (dp.TileX != tx || dp.TileY != ty) continue;
            d.DeleteEntity();
            ClearStructureWorkers(structureId, wKind);
            return;
        }
        _world.SpawnDesignation(tx, ty, dKind);
    }

    private void ClearStructureWorkers(int structureId, WorkKind wKind)
    {
        foreach (var c in _world.Store.Query<Colonist, WorkJob, PathFollower>().Entities)
        {
            ref var w = ref c.GetComponent<WorkJob>();
            if (!w.Active || w.Kind != wKind || w.TargetEntityId != structureId) continue;
            ref var pf = ref c.GetComponent<PathFollower>();
            ResetWorkJob(ref w, ref pf);
        }
    }

    private void UnblockFootprintIfStructure(BlueprintDef def, int rotation, int tileX, int tileY)
    {
        if (def.Category != BlueprintCategory.Structure) return;
        var (footW, footH) = (rotation & 1) == 0 ? (def.FootprintW, def.FootprintH) : (def.FootprintH, def.FootprintW);
        for (var dy = 0; dy < footH; dy++)
        {
            for (var dx = 0; dx < footW; dx++)
            {
                _grid.MarkBlocked(tileX + dx, tileY + dy, false);
            }
        }
    }

    private void Apply(CancelBlueprintCommand cmd)
    {
        var bp = _world.Store.GetEntityById(cmd.EntityId);
        if (bp == default || !bp.HasComponent<BlueprintGhost>() || !bp.HasComponent<TilePosition>()) return;
        ref var g = ref bp.GetComponent<BlueprintGhost>();
        ref var pos = ref bp.GetComponent<TilePosition>();
        var def = BlueprintCatalog.Get(g.DefId);

        if (g.MinifiedDelivered)
        {
            _world.SpawnMinifiedThing(g.DefId, pos.TileX, pos.TileY, g.Rotation, g.BaseLayer);
        }
        else
        {
            var deposited = g.MaterialDeposited;
            var mats = def.MaterialsOrEmpty;
            for (var i = 0; i < mats.Count && deposited > 0; i++)
            {
                var m = mats[i];
                var drop = Math.Min(m.Count, deposited);
                if (drop > 0) _world.AddOrMergeItem(pos.TileX, pos.TileY, m.Kind, drop);
                deposited -= drop;
            }
        }

        var query = _world.Store.Query<Colonist, WorkJob>();
        foreach (var entity in query.Entities)
        {
            ref var w = ref entity.GetComponent<WorkJob>();
            if (!w.Active) continue;
            var clear = false;
            if (w.Kind == WorkKind.Construct && w.TargetEntityId == cmd.EntityId) clear = true;
            else if (w.Kind == WorkKind.HaulToBlueprint
                && w.DropTileX == pos.TileX && w.DropTileY == pos.TileY) clear = true;
            if (!clear) continue;
            if (w.Kind == WorkKind.HaulToBlueprint)
            {
                DrainUnlockedToTile(entity, w.CarryKind);
            }
            w.Active = false;
            w.Kind = WorkKind.None;
            w.TargetEntityId = 0;
            w.Carrying = false;
            w.CarryKind = ItemKind.None;
            w.CarryCount = 0;
            w.CarryMinifiedDefId = null;
        }

        bp.DeleteEntity();
    }

    private void Apply(PrioritizeHaulCommand cmd)
    {
        var colonist = _world.Store.GetEntityById(cmd.ColonistId);
        if (colonist == default) return;
        if (!colonist.HasComponent<WorkJob>() || !colonist.HasComponent<PathFollower>()
            || !colonist.HasComponent<TilePosition>()) return;

        var item = _world.Store.GetEntityById(cmd.ItemEntityId);
        if (item == default || !item.HasComponent<Item>() || !item.HasComponent<TilePosition>()) return;
        ref var itComp = ref item.GetComponent<Item>();
        if (itComp.Forbidden) return;
        ref var itPos = ref item.GetComponent<TilePosition>();
        var ix = itPos.TileX;
        var iy = itPos.TileY;

        if (!TryFindDropTile(itComp.Kind, out var dropX, out var dropY)) return;

        // Clear any other colonist already hauling this item; if they were
        // mid-carry, drop the payload where they stand so it doesn't vanish.
        foreach (var other in _world.Store.Query<Colonist, WorkJob, PathFollower, TilePosition>().Entities)
        {
            if (other.Id == cmd.ColonistId) continue;
            ref var ow = ref other.GetComponent<WorkJob>();
            if (!ow.Active || ow.Kind != WorkKind.HaulItem || ow.TargetEntityId != cmd.ItemEntityId) continue;
            ref var opf = ref other.GetComponent<PathFollower>();
            DrainUnlockedToTile(other, ow.CarryKind);
            ResetWorkJob(ref ow, ref opf);
        }

        ref var work = ref colonist.GetComponent<WorkJob>();
        ref var pf = ref colonist.GetComponent<PathFollower>();
        work.Active = true;
        work.Kind = WorkKind.HaulItem;
        work.TargetEntityId = cmd.ItemEntityId;
        work.TargetTileX = ix;
        work.TargetTileY = iy;
        work.DropTileX = dropX;
        work.DropTileY = dropY;
        work.Progress = 0f;
        work.Forced = true;
        work.Carrying = false;
        work.CarryKind = ItemKind.None;
        work.CarryCount = 0;
        pf.Tiles = null;
        pf.Index = 0;
    }

    private void Apply(SetItemForbiddenCommand cmd)
    {
        var item = _world.Store.GetEntityById(cmd.ItemEntityId);
        if (item == default || !item.HasComponent<Item>()) return;
        ref var it = ref item.GetComponent<Item>();
        it.Forbidden = cmd.Forbidden;
        if (!cmd.Forbidden) return;

        // Forbidding clears every haul job pointing at this stack.
        // If a colonist had already pulled it into Inventory, drain the
        // unlocked stacks of that kind onto their tile so the stack
        // count survives.
        foreach (var other in _world.Store.Query<Colonist, WorkJob, PathFollower, TilePosition>().Entities)
        {
            ref var ow = ref other.GetComponent<WorkJob>();
            if (!ow.Active || ow.Kind != WorkKind.HaulItem || ow.TargetEntityId != cmd.ItemEntityId) continue;
            ref var opf = ref other.GetComponent<PathFollower>();
            DrainUnlockedToTile(other, ow.CarryKind);
            ResetWorkJob(ref ow, ref opf);
        }
    }

    private bool TryFindDropTile(ItemKind kind, out int dropX, out int dropY)
    {
        dropX = 0;
        dropY = 0;
        var bestPriority = int.MinValue;
        var bestPartialFill = -1;
        var found = false;
        var bestX = 0;
        var bestY = 0;

        // Snapshot existing items keyed by tile so we can prefer
        // partial-fill stacks of the same kind over empty tiles. Multiple
        // entities can share a tile (mixed-kind drops) so this is a
        // multi-map, not a last-writer-wins single value.
        var itemsByTile = new Dictionary<(int, int), List<(ItemKind kind, int count, int capacity)>>();
        foreach (var entity in _world.Store.Query<Item, TilePosition>().Entities)
        {
            ref var it = ref entity.GetComponent<Item>();
            ref var pos = ref entity.GetComponent<TilePosition>();
            var key = (pos.TileX, pos.TileY);
            if (!itemsByTile.TryGetValue(key, out var list))
            {
                list = new List<(ItemKind, int, int)>(1);
                itemsByTile[key] = list;
            }
            list.Add((it.Kind, it.Count, it.Capacity));
        }

        foreach (var entity in _world.Store.Query<Zone>().Entities)
        {
            ref var z = ref entity.GetComponent<Zone>();
            if (z.Type != ZoneType.Stockpile) continue;
            var priority = 0;
            var allowedMask = StockpileFilter.DefaultMask;
            if (entity.HasComponent<StockpileSettings>())
            {
                ref var s = ref entity.GetComponent<StockpileSettings>();
                priority = s.Priority;
                allowedMask = s.AllowedKindsMask;
            }
            if (!StockpileFilter.MaskAccepts(allowedMask, kind)) continue;
            if (priority < bestPriority) continue;

            for (var ty = z.Rect.MinY; ty <= z.Rect.MaxY; ty++)
            {
                for (var tx = z.Rect.MinX; tx <= z.Rect.MaxX; tx++)
                {
                    if (!z.ContainsTile(tx, ty)) continue;
                    if ((uint)tx >= (uint)_grid.Width || (uint)ty >= (uint)_grid.Height) continue;
                    if (_grid.IsBlocked(tx, ty)) continue;
                    var partial = -1;
                    if (itemsByTile.TryGetValue((tx, ty), out var existing))
                    {
                        var ok = true;
                        var bestRoom = -1;
                        var bestExistingCount = -1;
                        for (var ei = 0; ei < existing.Count; ei++)
                        {
                            var ex = existing[ei];
                            if (ex.kind != kind) { ok = false; break; }
                            var room = ex.capacity - ex.count;
                            if (room > bestRoom)
                            {
                                bestRoom = room;
                                bestExistingCount = ex.count;
                            }
                        }
                        if (!ok) continue;
                        if (bestRoom <= 0) continue;
                        partial = bestExistingCount;
                    }
                    var better = priority > bestPriority
                        || (priority == bestPriority && partial > bestPartialFill);
                    if (!better) continue;
                    bestPriority = priority;
                    bestPartialFill = partial;
                    bestX = tx;
                    bestY = ty;
                    found = true;
                }
            }
        }
        if (!found) return false;
        dropX = bestX;
        dropY = bestY;
        return true;
    }

    // Drop every unlocked, non-equipped inventory stack of `kind` at the
    // colonist's tile. Used when pre-empting a haul (auto-haul, haul-to-
    // blueprint) — the carried payload now lives in Inventory, so the
    // pre-empt path has to drain it instead of reading the legacy
    // WorkJob.CarryCount counter (which haul systems stopped writing).
    private void DrainUnlockedToTile(Entity colonist, ItemKind kind)
    {
        if (kind == ItemKind.None) return;
        if (!colonist.HasComponent<Inventory>() || !colonist.HasComponent<TilePosition>()) return;
        ref var inv = ref colonist.GetComponent<Inventory>();
        ref var pos = ref colonist.GetComponent<TilePosition>();
        if (inv.Stacks is null) return;
        for (var i = inv.Stacks.Count - 1; i >= 0; i--)
        {
            var s = inv.Stacks[i];
            if (s.Locked || s.Equipped) continue;
            var def = ItemCatalog.Get(s.DefId);
            if (def.Kind != kind) continue;
            if (def.Kind == ItemKind.Minified)
            {
                if (!string.IsNullOrEmpty(s.WrappedDefId))
                    _world.SpawnMinifiedThing(s.WrappedDefId, pos.TileX, pos.TileY, 0, 0);
            }
            else
            {
                _world.AddOrMergeItem(pos.TileX, pos.TileY, def.Kind, s.Count);
            }
            inv.Stacks.RemoveAt(i);
        }
    }

    private static void ResetWorkJob(ref WorkJob work, ref PathFollower pf)
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

    private void Apply(PrioritizeChopCommand cmd)
    {
        var colonist = _world.Store.GetEntityById(cmd.ColonistId);
        if (colonist == default) return;
        if (!colonist.HasComponent<WorkJob>() || !colonist.HasComponent<PathFollower>()) return;

        var tree = _world.Store.GetEntityById(cmd.TreeEntityId);
        if (tree == default || !tree.HasComponent<Tree>() || !tree.HasComponent<TilePosition>()) return;

        ref var treePos = ref tree.GetComponent<TilePosition>();
        var tx = treePos.TileX;
        var ty = treePos.TileY;

        // Clear any other colonist already targeting this tree. Two
        // colonists chopping the same trunk both reach Health<=0 on the
        // same tick, push duplicate FelledTree entries, and the second
        // pass crashed the sim thread. Single-assignment per tree is
        // also what the player asked for ergonomically.
        foreach (var other in _world.Store.Query<Colonist, WorkJob, PathFollower>().Entities)
        {
            if (other.Id == cmd.ColonistId) continue;
            ref var ow = ref other.GetComponent<WorkJob>();
            if (!ow.Active || ow.TargetEntityId != cmd.TreeEntityId) continue;
            ref var opf = ref other.GetComponent<PathFollower>();
            ow.Active = false;
            ow.Kind = WorkKind.None;
            ow.TargetEntityId = 0;
            ow.Progress = 0f;
            ow.Forced = false;
            opf.Tiles = null;
            opf.Index = 0;
        }

        // Stamp a chop designation if one isn't there yet so ChopJobSystem's
        // CollectChopDesignations sees the tile. Players using prioritize
        // shouldn't have to also click "designate".
        var hasDesignation = false;
        foreach (var entity in _world.Store.Query<Designation, TilePosition>().Entities)
        {
            ref var d = ref entity.GetComponent<Designation>();
            if (d.Kind != DesignationKind.ChopTree) continue;
            ref var p = ref entity.GetComponent<TilePosition>();
            if (p.TileX == tx && p.TileY == ty) { hasDesignation = true; break; }
        }
        if (!hasDesignation) _world.SpawnDesignation(tx, ty, DesignationKind.ChopTree);

        ref var work = ref colonist.GetComponent<WorkJob>();
        ref var pf = ref colonist.GetComponent<PathFollower>();
        work.Active = true;
        work.Kind = WorkKind.ChopTree;
        work.TargetEntityId = cmd.TreeEntityId;
        work.TargetTileX = tx;
        work.TargetTileY = ty;
        work.Progress = 0f;
        work.Forced = true;
        pf.Tiles = null;
        pf.Index = 0;
    }

    private void Apply(PrioritizeMineCommand cmd)
    {
        var colonist = _world.Store.GetEntityById(cmd.ColonistId);
        if (colonist == default) return;
        if (!colonist.HasComponent<WorkJob>() || !colonist.HasComponent<PathFollower>()) return;

        var boulder = _world.Store.GetEntityById(cmd.BoulderEntityId);
        if (boulder == default || !boulder.HasComponent<Boulder>() || !boulder.HasComponent<TilePosition>()) return;

        ref var bPos = ref boulder.GetComponent<TilePosition>();
        var tx = bPos.TileX;
        var ty = bPos.TileY;

        // Single-assignment per boulder — same anti-double-finish reason
        // PrioritizeChop uses. Two miners on one rock both reach Health<=0
        // on the same tick and the second yield-spawn crashes.
        foreach (var other in _world.Store.Query<Colonist, WorkJob, PathFollower>().Entities)
        {
            if (other.Id == cmd.ColonistId) continue;
            ref var ow = ref other.GetComponent<WorkJob>();
            if (!ow.Active || ow.TargetEntityId != cmd.BoulderEntityId) continue;
            ref var opf = ref other.GetComponent<PathFollower>();
            ow.Active = false;
            ow.Kind = WorkKind.None;
            ow.TargetEntityId = 0;
            ow.Progress = 0f;
            ow.Forced = false;
            opf.Tiles = null;
            opf.Index = 0;
        }

        var hasDesignation = false;
        foreach (var entity in _world.Store.Query<Designation, TilePosition>().Entities)
        {
            ref var d = ref entity.GetComponent<Designation>();
            if (d.Kind != DesignationKind.Mine) continue;
            ref var p = ref entity.GetComponent<TilePosition>();
            if (p.TileX == tx && p.TileY == ty) { hasDesignation = true; break; }
        }
        if (!hasDesignation) _world.SpawnDesignation(tx, ty, DesignationKind.Mine);

        ref var work = ref colonist.GetComponent<WorkJob>();
        ref var pf = ref colonist.GetComponent<PathFollower>();
        work.Active = true;
        work.Kind = WorkKind.Mine;
        work.TargetEntityId = cmd.BoulderEntityId;
        work.TargetTileX = tx;
        work.TargetTileY = ty;
        work.Progress = 0f;
        work.Forced = true;
        pf.Tiles = null;
        pf.Index = 0;
    }

    private void Apply(PrioritizeBuildCommand cmd)
    {
        var colonist = _world.Store.GetEntityById(cmd.ColonistId);
        if (colonist == default) return;
        if (!colonist.HasComponent<WorkJob>() || !colonist.HasComponent<PathFollower>()
            || !colonist.HasComponent<TilePosition>()) return;

        var bp = _world.Store.GetEntityById(cmd.BlueprintEntityId);
        if (bp == default || !bp.HasComponent<BlueprintGhost>() || !bp.HasComponent<TilePosition>()) return;

        ref var ghost = ref bp.GetComponent<BlueprintGhost>();
        ref var bpPos = ref bp.GetComponent<TilePosition>();
        var bx = bpPos.TileX;
        var by = bpPos.TileY;

        if (!BlueprintCatalog.TryGet(ghost.DefId, out var def) || def is null) return;
        var requiredWood = 0;
        var mats = def.MaterialsOrEmpty;
        for (var i = 0; i < mats.Count; i++)
            if (mats[i].Kind == ItemKind.Wood) requiredWood += mats[i].Count;

        // Boot any other colonist mid-haul/mid-construct on this same
        // blueprint so two colonists don't race the same tile.
        foreach (var other in _world.Store.Query<Colonist, WorkJob, PathFollower, TilePosition>().Entities)
        {
            if (other.Id == cmd.ColonistId) continue;
            ref var ow = ref other.GetComponent<WorkJob>();
            if (!ow.Active) continue;
            var matches = false;
            if (ow.Kind == WorkKind.Construct && ow.TargetEntityId == cmd.BlueprintEntityId) matches = true;
            else if (ow.Kind == WorkKind.HaulToBlueprint && ow.DropTileX == bx && ow.DropTileY == by) matches = true;
            if (!matches) continue;
            ref var opf = ref other.GetComponent<PathFollower>();
            if (ow.Kind == WorkKind.HaulToBlueprint) DrainUnlockedToTile(other, ow.CarryKind);
            ResetWorkJob(ref ow, ref opf);
        }

        ref var work = ref colonist.GetComponent<WorkJob>();
        ref var pf = ref colonist.GetComponent<PathFollower>();

        // Already-deposited blueprint → just go construct.
        if (ghost.MinifiedDelivered || ghost.MaterialDeposited >= requiredWood)
        {
            work.Active = true;
            work.Kind = WorkKind.Construct;
            work.TargetEntityId = cmd.BlueprintEntityId;
            work.TargetTileX = bx;
            work.TargetTileY = by;
            work.DropTileX = 0;
            work.DropTileY = 0;
            work.Progress = 0f;
            work.Forced = true;
            work.Carrying = false;
            work.CarryKind = ItemKind.None;
            work.CarryCount = 0;
            work.CarryMinifiedDefId = null;
            pf.Tiles = null;
            pf.Index = 0;
            return;
        }

        // Needs material. Find the nearest viable wood (or matching
        // minified) item to grab as the first pickup.
        ref var cPos = ref colonist.GetComponent<TilePosition>();
        var bestId = 0;
        var bestX = 0;
        var bestY = 0;
        var bestDist = float.PositiveInfinity;
        var bestIsMini = false;
        if (ghost.MaterialDeposited == 0)
        {
            foreach (var entity in _world.Store.Query<Item, TilePosition>().Entities)
            {
                ref var it = ref entity.GetComponent<Item>();
                if (it.Kind != ItemKind.Minified || it.Forbidden) continue;
                if (!entity.HasComponent<MinifiedThing>()) continue;
                ref var m = ref entity.GetComponent<MinifiedThing>();
                if (m.DefId != ghost.DefId) continue;
                ref var ipos = ref entity.GetComponent<TilePosition>();
                var dx = ipos.TileX - cPos.TileX;
                var dy = ipos.TileY - cPos.TileY;
                var d = dx * dx + dy * dy;
                if (d < bestDist) { bestDist = d; bestId = entity.Id; bestX = ipos.TileX; bestY = ipos.TileY; bestIsMini = true; }
            }
        }
        if (bestId == 0)
        {
            foreach (var entity in _world.Store.Query<Item, TilePosition>().Entities)
            {
                ref var it = ref entity.GetComponent<Item>();
                if (it.Kind != ItemKind.Wood || it.Forbidden) continue;
                ref var ipos = ref entity.GetComponent<TilePosition>();
                var dx = ipos.TileX - cPos.TileX;
                var dy = ipos.TileY - cPos.TileY;
                var d = dx * dx + dy * dy;
                if (d < bestDist) { bestDist = d; bestId = entity.Id; bestX = ipos.TileX; bestY = ipos.TileY; bestIsMini = false; }
            }
        }
        if (bestId == 0) return;

        work.Active = true;
        work.Kind = WorkKind.HaulToBlueprint;
        work.TargetEntityId = bestId;
        work.TargetTileX = bestX;
        work.TargetTileY = bestY;
        work.DropTileX = bx;
        work.DropTileY = by;
        work.Progress = 0f;
        work.Forced = true;
        work.Carrying = false;
        work.CarryKind = ItemKind.None;
        work.CarryCount = 0;
        work.CarryMinifiedDefId = null;
        pf.Tiles = null;
        pf.Index = 0;
        _ = bestIsMini;
    }

    private void Apply(EraseInRectCommand cmd)
    {
        var rect = ClampRect(cmd.Rect);
        var toDelete = new List<int>();

        var zoneEdits = new List<(int Id, TileRect Rect, bool[] Mask)>();
        var stockpileTopologyChanged = false;
        foreach (var entity in _world.Store.Query<Zone>().Entities)
        {
            ref var z = ref entity.GetComponent<Zone>();
            if (!RectsOverlap(z.Rect, rect)) continue;
            var result = TileMask.SubtractRect(z.Rect, z.Mask, rect);
            if (z.Type == ZoneType.Stockpile) stockpileTopologyChanged = true;
            if (result is null) toDelete.Add(entity.Id);
            else zoneEdits.Add((entity.Id, result.Value.Item1, result.Value.Item2));
        }
        foreach (var (id, newRect, newMask) in zoneEdits)
        {
            var e = _world.Store.GetEntityById(id);
            if (e == default) continue;
            ref var z = ref e.GetComponent<Zone>();
            z.Rect = newRect;
            z.Mask = newMask;
        }
        foreach (var entity in _world.Store.Query<Designation, TilePosition>().Entities)
        {
            ref var p = ref entity.GetComponent<TilePosition>();
            if (rect.Contains(p.TileX, p.TileY)) toDelete.Add(entity.Id);
        }
        // Blueprints inside the erase rect go through CancelBlueprintCommand
        // so deposited materials drop, in-flight haulers drain their carry,
        // and any colonist in Construct/HaulToBlueprint state is cleared.
        // Plain DeleteEntity stranded carries and orphaned WorkJobs.
        var blueprintsToCancel = new List<int>();
        foreach (var entity in _world.Store.Query<BlueprintGhost>().Entities)
        {
            ref var g = ref entity.GetComponent<BlueprintGhost>();
            if (rect.Contains(g.OriginTileX, g.OriginTileY)) blueprintsToCancel.Add(entity.Id);
        }

        foreach (var id in toDelete)
        {
            var e = _world.Store.GetEntityById(id);
            if (e != default) e.DeleteEntity();
        }
        for (var i = 0; i < blueprintsToCancel.Count; i++)
        {
            Apply(new CancelBlueprintCommand(blueprintsToCancel[i]));
        }
        if (stockpileTopologyChanged) _world.BumpStockpileVersion();
    }

    private static bool RectsOverlap(TileRect a, TileRect b)
        => a.MinX <= b.MaxX && a.MaxX >= b.MinX
        && a.MinY <= b.MaxY && a.MaxY >= b.MinY;

    private void Apply(CreateZoneCommand cmd)
    {
        var rect = ClampRect(cmd.Rect);
        if (rect.Width <= 0 || rect.Height <= 0) return;

        // Merge any same-type zones whose mask actually intersects the
        // newly-drawn tiles (transitively, so chain merges collapse in
        // one pass). The first overlapping zone keeps its entity +
        // settings; the rest are absorbed into it. Different zone types
        // never merge — they coexist.
        var bbox = rect;
        var mask = TileMask.Filled(rect);
        var overlapping = new List<int>();
        bool grew;
        do
        {
            grew = false;
            foreach (var entity in _world.Store.Query<Zone>().Entities)
            {
                if (overlapping.Contains(entity.Id)) continue;
                ref var z = ref entity.GetComponent<Zone>();
                if (z.Type != cmd.Type) continue;
                if (!TileMask.Intersects(z.Rect, z.Mask, bbox, mask)) continue;
                (bbox, mask) = TileMask.Union(bbox, mask, z.Rect, z.Mask);
                overlapping.Add(entity.Id);
                grew = true;
            }
        } while (grew);

        if (overlapping.Count > 0)
        {
            var primary = _world.Store.GetEntityById(overlapping[0]);
            ref var pz = ref primary.GetComponent<Zone>();
            pz.Rect = bbox;
            pz.Mask = mask;
            for (var i = 1; i < overlapping.Count; i++)
            {
                var e = _world.Store.GetEntityById(overlapping[i]);
                if (e != default) e.DeleteEntity();
            }
            if (cmd.Type == ZoneType.Stockpile) _world.BumpStockpileVersion();
            return;
        }

        var spawned = _world.SpawnZone(0, cmd.Type, bbox, mask, cmd.Name);
        ref var sz = ref spawned.GetComponent<Zone>();
        sz.ZoneId = spawned.Id;
    }

    private void Apply(StampDesignationsCommand cmd)
    {
        var rect = ClampRect(cmd.Rect);
        for (var y = rect.MinY; y <= rect.MaxY; y++)
        {
            for (var x = rect.MinX; x <= rect.MaxX; x++)
            {
                _world.SpawnDesignation(x, y, cmd.Kind);
            }
        }
    }

    private void Apply(PlaceBlueprintGhostCommand cmd)
    {
        if (!BlueprintCatalog.TryGet(cmd.DefId, out var def) || def is null) return;
        var (footW, footH) = (cmd.Rotation & 1) == 0
            ? (def.FootprintW, def.FootprintH)
            : (def.FootprintH, def.FootprintW);
        if (!FootprintInBounds(cmd.OriginTileX, cmd.OriginTileY, footW, footH)) return;
        if (cmd.BaseLayer == 0 && !FootprintLevel(cmd.OriginTileX, cmd.OriginTileY, footW, footH)) return;
        if (FootprintObstructed(cmd.OriginTileX, cmd.OriginTileY, footW, footH, cmd.BaseLayer, def.HeightQuanta)) return;
        _world.SpawnBlueprintGhost(cmd.DefId, cmd.OriginTileX, cmd.OriginTileY, cmd.Rotation, cmd.BaseLayer);
    }

    private bool FootprintInBounds(int ox, int oy, int w, int h)
        => ox >= 0 && oy >= 0 && ox + w <= _grid.Width && oy + h <= _grid.Height;

    // All vertex corners spanning the footprint must share one quantum value
    // — the 4-unshared-corners-per-tile rendering means a footprint over a
    // sloped tile would visibly hover, and pathing on top of it would lie.
    private bool FootprintLevel(int ox, int oy, int w, int h)
    {
        var anchor = _grid.CornerQuanta(ox, oy);
        for (var vy = oy; vy <= oy + h; vy++)
        {
            for (var vx = ox; vx <= ox + w; vx++)
            {
                if (_grid.CornerQuanta(vx, vy) != anchor) return false;
            }
        }
        return true;
    }

    private bool FootprintObstructed(int ox, int oy, int w, int h, int baseLayer, int heightQuanta)
    {
        var foot = new TileRect(ox, oy, ox + w - 1, oy + h - 1);
        var topLayer = baseLayer + heightQuanta;
        foreach (var entity in _world.Store.Query<BlueprintGhost>().Entities)
        {
            ref var g = ref entity.GetComponent<BlueprintGhost>();
            if (!BlueprintCatalog.TryGet(g.DefId, out var od) || od is null) continue;
            var existingTop = g.BaseLayer + od.HeightQuanta;
            if (baseLayer >= existingTop || topLayer <= g.BaseLayer) continue;
            var (ow, oh) = (g.Rotation & 1) == 0
                ? (od.FootprintW, od.FootprintH)
                : (od.FootprintH, od.FootprintW);
            var other = new TileRect(g.OriginTileX, g.OriginTileY, g.OriginTileX + ow - 1, g.OriginTileY + oh - 1);
            if (RectsOverlap(foot, other)) return true;
        }
        return false;
    }

    private void Apply(SetZoneSettingsCommand cmd)
    {
        var entity = _world.Store.GetEntityById(cmd.ZoneId);
        if (entity == default || !entity.HasComponent<Zone>()) return;
        ref var z = ref entity.GetComponent<Zone>();
        z.Name = cmd.Name ?? string.Empty;
        if (entity.HasComponent<StockpileSettings>())
        {
            ref var s = ref entity.GetComponent<StockpileSettings>();
            s.Priority = cmd.Priority;
            s.AllowedKindsMask = cmd.AllowedKindsMask;
            _world.BumpStockpileVersion();
        }
        if (entity.HasComponent<FarmSettings>())
        {
            ref var f = ref entity.GetComponent<FarmSettings>();
            f.CropDefId = cmd.CropDefId;
            f.AllowSowing = cmd.AllowSowing;
            f.AllowHarvest = cmd.AllowHarvest;
        }
    }

    private TileRect ClampRect(TileRect rect)
    {
        var w = _grid.Width;
        var h = _grid.Height;
        var minX = Math.Clamp(rect.MinX, 0, w - 1);
        var minY = Math.Clamp(rect.MinY, 0, h - 1);
        var maxX = Math.Clamp(rect.MaxX, 0, w - 1);
        var maxY = Math.Clamp(rect.MaxY, 0, h - 1);
        return new TileRect(minX, minY, maxX, maxY);
    }

    private void Apply(InvalidatePathsInRegion region)
    {
        var query = _world.Store.Query<PathFollower>();
        foreach (var entity in query.Entities)
        {
            ref var pf = ref entity.GetComponent<PathFollower>();
            if (pf.Tiles is null) continue;
            for (var i = pf.Index; i < pf.Tiles.Length; i++)
            {
                var t = pf.Tiles[i];
                if (t.X >= region.MinTileX && t.X <= region.MaxTileX
                    && t.Y >= region.MinTileY && t.Y <= region.MaxTileY)
                {
                    pf.Tiles = null;
                    pf.Index = 0;
                    break;
                }
            }
        }
    }

    private void Apply(MoveCommand move)
    {
        var entity = _world.Store.GetEntityById(move.EntityId);
        if (entity == default) return;
        if (!entity.HasComponent<PathFollower>()) return;
        if (!entity.HasComponent<TilePosition>()) return;
        if (!_grid.InBounds(move.Target)) return;
        if (!entity.HasComponent<Drafted>() || !entity.GetComponent<Drafted>().Active) return;

        ref var pos = ref entity.GetComponent<TilePosition>();
        ref var pf = ref entity.GetComponent<PathFollower>();
        var start = _grid.At(
            Math.Clamp(pos.TileX, 0, _grid.Width - 1),
            Math.Clamp(pos.TileY, 0, _grid.Height - 1));
        var goal = _grid.At(move.Target.X, move.Target.Y);
        pf.Tiles = null;
        pf.Index = 0;
        pf.PendingRequest = true;
        pf.PlayerForced = true;
        _planner.Request(entity.Id, start, goal);
    }
}
