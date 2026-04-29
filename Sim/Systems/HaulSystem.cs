using CowColonySim.Sim.Designations;
using CowColonySim.Sim.Items;
using CowColonySim.Sim.Logging;
using CowColonySim.Sim.Pathfinding;
using CowColonySim.Sim.World;
using CowColonySim.Sim.World.Components;
using CowColonySim.Sim.Zones;
using Friflo.Engine.ECS;

namespace CowColonySim.Sim.Systems;

// Auto-haul: idle colonists pull non-forbidden item stacks that aren't
// already in a stockpile into their Inventory, chain-pick more of the
// same kind while bulk/weight room remains, then walk to the best
// stockpile tile and drain the carried stacks back to the ground.
//
// Inventory routing matters: locked + equipped stacks survive the
// drain, so a force-picked log stays put and worn gear isn't tossed.
// All entity creation/deletion is deferred outside the colonist loop.
public sealed class HaulSystem : ITickSystem
{
    // Chain pickups bound by this many tiles from the colonist's current
    // tile. Keeps haulers from sprinting across the map for one stray log
    // when their inventory has room left.
    private const int ChainRadiusTiles = 12;
    private const int ChainRadiusTilesSq = ChainRadiusTiles * ChainRadiusTiles;

    private readonly SimWorld _world;
    private readonly PathPlanner _planner;
    private readonly HeightGrid _grid;

    private readonly List<int> _pickupsToDelete = new();
    private readonly List<DepositAction> _deposits = new();
    private readonly List<RespawnAction> _partialRespawns = new();

    private readonly struct DepositAction
    {
        public readonly int TileX;
        public readonly int TileY;
        public readonly ItemKind Kind;
        public readonly int Count;
        public readonly string MinifiedDefId;
        public DepositAction(int tx, int ty, ItemKind kind, int count, string miniDef)
        { TileX = tx; TileY = ty; Kind = kind; Count = count; MinifiedDefId = miniDef; }
    }

    // Partial pickup leaves a leftover. We delete the source entity and
    // respawn a fresh one with the same metadata so downstream lookups
    // (HaulSystem byTile, UI, reservations) treat it as a brand new stack.
    private readonly struct RespawnAction
    {
        public readonly int TileX;
        public readonly int TileY;
        public readonly ItemKind Kind;
        public readonly int Count;
        public readonly int Capacity;
        public readonly bool Forbidden;
        public readonly bool HasMinified;
        public readonly string MinifiedDefId;
        public readonly int MinifiedRotation;
        public readonly int MinifiedBaseLayer;
        public RespawnAction(int tx, int ty, ItemKind kind, int count, int capacity, bool forbidden,
            bool hasMini, string miniDef, int miniRot, int miniLayer)
        {
            TileX = tx; TileY = ty; Kind = kind; Count = count; Capacity = capacity;
            Forbidden = forbidden; HasMinified = hasMini; MinifiedDefId = miniDef;
            MinifiedRotation = miniRot; MinifiedBaseLayer = miniLayer;
        }
    }

    public HaulSystem(SimWorld world, PathPlanner planner, HeightGrid grid)
    {
        _world = world;
        _planner = planner;
        _grid = grid;
    }

    public void Tick(TickContext ctx)
    {
        _pickupsToDelete.Clear();
        _deposits.Clear();
        _partialRespawns.Clear();

        var stockpileTiles = CollectStockpileTiles();
        var (itemsByTile, itemsByEntity) = CollectItems();

        var claimedItems = new HashSet<int>();
        var occupiedDropTiles = new HashSet<(int, int)>();
        var query = _world.Store.Query<Colonist, Job, WorkJob, TilePosition, PathFollower>();
        foreach (var entity in query.Entities)
        {
            ref var work = ref entity.GetComponent<WorkJob>();
            if (!work.Active || work.Kind != WorkKind.HaulItem) continue;
            if (work.TargetEntityId != 0) claimedItems.Add(work.TargetEntityId);
            occupiedDropTiles.Add((work.DropTileX, work.DropTileY));
        }

        foreach (var entity in query.Entities)
        {
            ref var job = ref entity.GetComponent<Job>();
            if (job.Active) continue;
            if (!entity.HasComponent<Inventory>() || !entity.HasComponent<CarryCaps>()) continue;
            ref var work = ref entity.GetComponent<WorkJob>();
            ref var pos = ref entity.GetComponent<TilePosition>();
            ref var pf = ref entity.GetComponent<PathFollower>();

            if (work.Active && work.Kind == WorkKind.HaulItem)
            {
                ProgressHaul(entity, ref work, ref pf, ref pos, itemsByTile, itemsByEntity, stockpileTiles, claimedItems, occupiedDropTiles);
            }
            else if (!work.Active)
            {
                TryAssignHaul(entity, ref work, ref pf, ref pos, itemsByTile, stockpileTiles, claimedItems, occupiedDropTiles);
            }
        }

        for (var i = 0; i < _pickupsToDelete.Count; i++)
        {
            var item = _world.Store.GetEntityById(_pickupsToDelete[i]);
            if (item != default) item.DeleteEntity();
        }
        for (var i = 0; i < _partialRespawns.Count; i++)
        {
            var r = _partialRespawns[i];
            if (r.Count <= 0) continue;
            if (r.HasMinified && !string.IsNullOrEmpty(r.MinifiedDefId))
            {
                _world.SpawnMinifiedThing(r.MinifiedDefId, r.TileX, r.TileY, r.MinifiedRotation, r.MinifiedBaseLayer);
                continue;
            }
            var e = _world.Store.CreateEntity();
            e.AddComponent(new TilePosition(r.TileX, r.TileY, 0, 0.5f, 0.5f));
            e.AddComponent(new Item { Kind = r.Kind, Count = r.Count, Capacity = r.Capacity, Forbidden = r.Forbidden });
        }
        for (var i = 0; i < _deposits.Count; i++)
        {
            var d = _deposits[i];
            if (d.Kind == ItemKind.Minified && !string.IsNullOrEmpty(d.MinifiedDefId))
                _world.SpawnMinifiedThing(d.MinifiedDefId, d.TileX, d.TileY, 0, 0);
            else
                _world.AddOrMergeItem(d.TileX, d.TileY, d.Kind, d.Count);
        }
    }

    private void ProgressHaul(
        Entity entity, ref WorkJob work, ref PathFollower pf, ref TilePosition pos,
        Dictionary<(int, int), List<ItemSnapshot>> itemsByTile,
        Dictionary<int, ItemSnapshot> itemsByEntity,
        Dictionary<(int, int), ulong> stockpileTiles,
        HashSet<int> claimedItems,
        HashSet<(int, int)> occupiedDropTiles)
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
                    DrainCarriedToTile(ref inv, pos.TileX, pos.TileY);
                    ClearWork(ref work, ref pf);
                    return;
                }
                EnsurePath(entity, ref pf, pos.TileX, pos.TileY, work.DropTileX, work.DropTileY);
                return;
            }
            SimLog.Logger.Information(
                "HAUL drop-arrived colonist={Cid} drop=({DX},{DY}) carryKind={K} stacks={Sc}",
                entity.Id, work.DropTileX, work.DropTileY, work.CarryKind,
                inv.Stacks?.Count ?? 0);
            // Drop only the current CarryKind here; if more kinds remain
            // in inv, retarget DropTile to the next kind's stockpile and
            // walk over.
            DrainKindToTile(ref inv, work.CarryKind, work.DropTileX, work.DropTileY);
            if (TryRetargetNextKindDrop(entity, ref work, ref pf, ref pos, ref inv,
                itemsByTile, occupiedDropTiles))
                return;
            // Anything left that had no stockpile fit — drop on the
            // current tile so it doesn't ride along forever.
            DrainCarriedToTile(ref inv, work.DropTileX, work.DropTileY);
            ClearWork(ref work, ref pf);
            return;
        }

        // Pickup phase
        if (!itemsByEntity.TryGetValue(work.TargetEntityId, out var item) || item.Forbidden)
        {
            // Source gone or forbidden — chain or finish.
            if (!TryChainNextPickup(entity, ref work, ref pf, ref pos, itemsByTile, stockpileTiles, claimedItems, in inv, in caps))
                SwitchToDropOrFinish(entity, ref work, ref pf, ref pos, ref inv);
            return;
        }
        if (pos.TileX != item.TileX || pos.TileY != item.TileY)
        {
            if (pf.LastPathFailed)
            {
                if (!TryChainNextPickup(entity, ref work, ref pf, ref pos, itemsByTile, stockpileTiles, claimedItems, in inv, in caps))
                    SwitchToDropOrFinish(entity, ref work, ref pf, ref pos, ref inv);
                return;
            }
            EnsurePath(entity, ref pf, pos.TileX, pos.TileY, item.TileX, item.TileY);
            return;
        }

        // At pickup tile — pull the whole stack (or as much as fits) into Inventory.
        // Minified routes through AddMinified so the wrapped blueprint id
        // travels in the inv stack — drop/drain needs it to call
        // SpawnMinifiedThing with the right structure def.
        var defId = ResolveDefId(item);
        int added;
        if (item.Kind == ItemKind.Minified)
            added = InventoryOps.AddMinified(ref inv, in caps, item.MinifiedDefId);
        else
            added = InventoryOps.Add(ref inv, in caps, defId, item.Count);
        SimLog.Logger.Information(
            "HAUL pickup colonist={Cid} at ({X},{Y}) defId={Def} item.Count={N} added={Added} drop=({DX},{DY})",
            entity.Id, pos.TileX, pos.TileY, defId, item.Count, added, work.DropTileX, work.DropTileY);
        if (added <= 0)
        {
            // Inventory full — switch to drop with what we already have.
            SwitchToDropOrFinish(entity, ref work, ref pf, ref pos, ref inv);
            return;
        }
        if (work.CarryKind == ItemKind.None) work.CarryKind = item.Kind;

        if (added < item.Count)
        {
            // Partial fill — delete the source entity and respawn a fresh
            // one with the leftover count. Mutating Count in place left the
            // same entity id with a "fake stack" feel that downstream
            // lookups (byTile last-writer-wins, UI cached views, future
            // reservations) sometimes failed to re-pick.
            var leftover = item.Count - added;
            var src = _world.Store.GetEntityById(work.TargetEntityId);
            if (src != default && src.HasComponent<Item>() && src.HasComponent<TilePosition>())
            {
                ref var srcIt = ref src.GetComponent<Item>();
                ref var srcPos = ref src.GetComponent<TilePosition>();
                var hasMini = src.HasComponent<MinifiedThing>();
                var miniDef = string.Empty;
                var miniRot = 0;
                var miniLayer = 0;
                if (hasMini)
                {
                    ref var m = ref src.GetComponent<MinifiedThing>();
                    miniDef = m.DefId;
                    miniRot = m.Rotation;
                    miniLayer = m.BaseLayer;
                }
                _partialRespawns.Add(new RespawnAction(
                    srcPos.TileX, srcPos.TileY, srcIt.Kind, leftover, srcIt.Capacity, srcIt.Forbidden,
                    hasMini, miniDef, miniRot, miniLayer));
            }
            _pickupsToDelete.Add(work.TargetEntityId);
            claimedItems.Add(work.TargetEntityId);
            SwitchToDropOrFinish(entity, ref work, ref pf, ref pos, ref inv);
            return;
        }

        // Whole stack consumed.
        _pickupsToDelete.Add(work.TargetEntityId);
        claimedItems.Add(work.TargetEntityId);

        if (!TryChainNextPickup(entity, ref work, ref pf, ref pos, itemsByTile, stockpileTiles, claimedItems, in inv, in caps))
            SwitchToDropOrFinish(entity, ref work, ref pf, ref pos, ref inv);
    }

    // Chain: nearest item of any kind in radius that still fits somewhere
    // in remaining inventory room. Mixed-kind hauls land at the original
    // CarryKind's drop tile; sorting back out is the next tick's problem.
    private bool TryChainNextPickup(
        Entity entity, ref WorkJob work, ref PathFollower pf, ref TilePosition pos,
        Dictionary<(int, int), List<ItemSnapshot>> itemsByTile,
        Dictionary<(int, int), ulong> stockpileTiles,
        HashSet<int> claimedItems,
        in Inventory inv, in CarryCaps caps)
    {
        var bestId = 0;
        var bestX = 0;
        var bestY = 0;
        var bestDist = float.PositiveInfinity;
        foreach (var kv in itemsByTile)
        {
            var list = kv.Value;
            for (var i = 0; i < list.Count; i++)
            {
                var it = list[i];
                if (it.Forbidden) continue;
                if (claimedItems.Contains(it.EntityId)) continue;
                if (StockpileAccepts(stockpileTiles, it.TileX, it.TileY, it.Kind)) continue;
                var defId = ResolveDefId(it);
                if (InventoryOps.RoomFor(defId, in caps, in inv) <= 0) continue;
                var dx = it.TileX - pos.TileX;
                var dy = it.TileY - pos.TileY;
                var d = dx * dx + dy * dy;
                if (d > ChainRadiusTilesSq) continue;
                if (d < bestDist)
                {
                    bestDist = d;
                    bestId = it.EntityId;
                    bestX = it.TileX;
                    bestY = it.TileY;
                }
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
        // Nothing actually carried? Just abort. Mixed-kind hauls count any
        // unlocked + unequipped stack as "holds" so we still walk to drop.
        var holds = false;
        if (inv.Stacks is not null)
        {
            for (var i = 0; i < inv.Stacks.Count; i++)
            {
                var s = inv.Stacks[i];
                if (s.Locked || s.Equipped) continue;
                if (s.Count <= 0) continue;
                holds = true;
                break;
            }
        }
        SimLog.Logger.Information(
            "HAUL switch-to-drop colonist={Cid} pos=({X},{Y}) carryKind={K} holds={H} drop=({DX},{DY})",
            entity.Id, pos.TileX, pos.TileY, work.CarryKind, holds, work.DropTileX, work.DropTileY);
        if (!holds)
        {
            ClearWork(ref work, ref pf);
            return;
        }
        work.TargetEntityId = 0;
        EnsurePath(entity, ref pf, pos.TileX, pos.TileY, work.DropTileX, work.DropTileY);
    }

    // Drain only stacks matching `kind` onto the tile. Other kinds stay
    // in the inventory for the next leg of the trip.
    private void DrainKindToTile(ref Inventory inv, ItemKind kind, int tileX, int tileY)
    {
        if (inv.Stacks is null || kind == ItemKind.None) return;
        for (var i = inv.Stacks.Count - 1; i >= 0; i--)
        {
            var s = inv.Stacks[i];
            if (s.Locked || s.Equipped) continue;
            var def = ItemCatalog.Get(s.DefId);
            if (def.Kind != kind) continue;
            var miniDef = def.Kind == ItemKind.Minified ? (s.WrappedDefId ?? string.Empty) : string.Empty;
            _deposits.Add(new DepositAction(tileX, tileY, def.Kind, s.Count, miniDef));
            inv.Stacks.RemoveAt(i);
        }
    }

    // After dropping the current kind, find the next kind still in inv
    // that has a viable stockpile tile and repath there. Returns false
    // when nothing left to deliver (or no stockpile tile fits any
    // remaining kind).
    private bool TryRetargetNextKindDrop(
        Entity entity, ref WorkJob work, ref PathFollower pf, ref TilePosition pos,
        ref Inventory inv,
        Dictionary<(int, int), List<ItemSnapshot>> itemsByTile,
        HashSet<(int, int)> occupiedDropTiles)
    {
        if (inv.Stacks is null) return false;
        for (var i = 0; i < inv.Stacks.Count; i++)
        {
            var s = inv.Stacks[i];
            if (s.Locked || s.Equipped) continue;
            if (s.Count <= 0) continue;
            var def = ItemCatalog.Get(s.DefId);
            if (!TryFindDropTile(def.Kind, s.Count, itemsByTile, occupiedDropTiles, out var nx, out var ny))
                continue;
            work.CarryKind = def.Kind;
            work.DropTileX = nx;
            work.DropTileY = ny;
            occupiedDropTiles.Add((nx, ny));
            EnsurePath(entity, ref pf, pos.TileX, pos.TileY, nx, ny);
            return true;
        }
        return false;
    }

    // Drain every non-locked, non-equipped inv stack onto a single tile.
    // Used for path-failure fallback where the colonist couldn't reach
    // their drop. Auto-haul will re-pick up the next tick.
    private void DrainCarriedToTile(ref Inventory inv, int tileX, int tileY)
    {
        if (inv.Stacks is null) return;
        for (var i = inv.Stacks.Count - 1; i >= 0; i--)
        {
            var s = inv.Stacks[i];
            if (s.Locked || s.Equipped) continue;
            var def = ItemCatalog.Get(s.DefId);
            if (def.Kind == ItemKind.Minified)
                _deposits.Add(new DepositAction(tileX, tileY, def.Kind, s.Count, s.WrappedDefId ?? string.Empty));
            else
                _deposits.Add(new DepositAction(tileX, tileY, def.Kind, s.Count, string.Empty));
            inv.Stacks.RemoveAt(i);
        }
    }

    private static string ResolveDefId(ItemSnapshot item)
    {
        if (item.Kind == ItemKind.Minified)
        {
            // Wrapped structure defs aren't in ItemCatalog — fall back to
            // the generic minified def for weight/bulk math, but we still
            // remember the wrapper id via the inventory entry's DefId so
            // reinstall can match. Phase-3: register per-wrapper defs.
            return ItemCatalog.TryGet(item.MinifiedDefId, out _)
                ? item.MinifiedDefId : ItemCatalog.DefaultIdFor(item.Kind);
        }
        return ItemCatalog.DefaultIdFor(item.Kind);
    }

    private void TryAssignHaul(
        Entity entity, ref WorkJob work, ref PathFollower pf, ref TilePosition pos,
        Dictionary<(int, int), List<ItemSnapshot>> itemsByTile, Dictionary<(int, int), ulong> stockpileTiles,
        HashSet<int> claimedItems, HashSet<(int, int)> occupiedDropTiles)
    {
        var bestItem = -1;
        var bestKind = ItemKind.None;
        var bestCount = 0;
        var bestItemTileX = 0;
        var bestItemTileY = 0;
        var bestDistSq = float.PositiveInfinity;
        foreach (var kv in itemsByTile)
        {
            var list = kv.Value;
            for (var i = 0; i < list.Count; i++)
            {
                var item = list[i];
                if (item.Forbidden) continue;
                if (claimedItems.Contains(item.EntityId)) continue;
                if (StockpileAccepts(stockpileTiles, item.TileX, item.TileY, item.Kind)) continue;
                var dx = item.TileX - pos.TileX;
                var dy = item.TileY - pos.TileY;
                var d = dx * dx + dy * dy;
                if (d < bestDistSq)
                {
                    bestDistSq = d;
                    bestItem = item.EntityId;
                    bestKind = item.Kind;
                    bestCount = item.Count;
                    bestItemTileX = item.TileX;
                    bestItemTileY = item.TileY;
                }
            }
        }
        if (bestItem == -1) return;

        if (!TryFindDropTile(bestKind, bestCount, itemsByTile, occupiedDropTiles, out var dropX, out var dropY))
        {
            SimLog.Logger.Information(
                "HAUL no-drop-tile colonist={Cid} kind={K} bestItem={B}",
                entity.Id, bestKind, bestItem);
            return;
        }
        SimLog.Logger.Information(
            "HAUL assign colonist={Cid} pos=({X},{Y}) item={I} src=({SX},{SY}) drop=({DX},{DY})",
            entity.Id, pos.TileX, pos.TileY, bestItem, bestItemTileX, bestItemTileY, dropX, dropY);

        work.Active = true;
        work.Kind = WorkKind.HaulItem;
        work.TargetEntityId = bestItem;
        work.TargetTileX = bestItemTileX;
        work.TargetTileY = bestItemTileY;
        work.DropTileX = dropX;
        work.DropTileY = dropY;
        work.Progress = 0f;
        work.Forced = false;
        work.Carrying = false;
        work.CarryKind = ItemKind.None;
        work.CarryCount = 0;
        work.CarryMinifiedDefId = null;
        claimedItems.Add(bestItem);
        occupiedDropTiles.Add((dropX, dropY));

        EnsurePath(entity, ref pf, pos.TileX, pos.TileY, bestItemTileX, bestItemTileY);
    }

    private bool TryFindDropTile(
        ItemKind kind, int count,
        Dictionary<(int, int), List<ItemSnapshot>> itemsByTile,
        HashSet<(int, int)> occupiedDropTiles,
        out int dropX, out int dropY)
    {
        dropX = 0;
        dropY = 0;
        var bestPriority = int.MinValue;
        var bestPartialFill = -1;
        var found = false;
        var bestX = 0;
        var bestY = 0;

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
                    if (occupiedDropTiles.Contains((tx, ty))) continue;
                    var partial = -1;
                    if (itemsByTile.TryGetValue((tx, ty), out var existing))
                    {
                        // Tile already has stuff. Only viable if every
                        // stack here is the same kind we're dropping AND
                        // at least one has room to merge into.
                        var ok = true;
                        var bestRoomSeen = -1;
                        var bestCountSeen = -1;
                        for (var ei = 0; ei < existing.Count; ei++)
                        {
                            var ex = existing[ei];
                            if (ex.Kind != kind) { ok = false; break; }
                            var room = ex.Capacity - ex.Count;
                            if (room > bestRoomSeen)
                            {
                                bestRoomSeen = room;
                                bestCountSeen = ex.Count;
                            }
                        }
                        if (!ok) continue;
                        if (bestRoomSeen <= 0) continue;
                        partial = bestCountSeen;
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

    private static bool StockpileAccepts(
        Dictionary<(int, int), ulong> tiles, int tx, int ty, ItemKind kind) =>
        tiles.TryGetValue((tx, ty), out var mask)
            && StockpileFilter.MaskAccepts(mask, kind);

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

    private readonly struct ItemSnapshot
    {
        public readonly int EntityId;
        public readonly ItemKind Kind;
        public readonly int Count;
        public readonly int Capacity;
        public readonly int TileX;
        public readonly int TileY;
        public readonly bool Forbidden;
        public readonly string MinifiedDefId;
        public ItemSnapshot(int id, ItemKind kind, int count, int capacity, int tx, int ty, bool forbidden, string miniDef)
        {
            EntityId = id;
            Kind = kind;
            Count = count;
            Capacity = capacity;
            TileX = tx;
            TileY = ty;
            Forbidden = forbidden;
            MinifiedDefId = miniDef;
        }
    }

    private (Dictionary<(int, int), List<ItemSnapshot>>, Dictionary<int, ItemSnapshot>) CollectItems()
    {
        var byTile = new Dictionary<(int, int), List<ItemSnapshot>>();
        var byEntity = new Dictionary<int, ItemSnapshot>();
        foreach (var entity in _world.Store.Query<Item, TilePosition>().Entities)
        {
            ref var it = ref entity.GetComponent<Item>();
            ref var pos = ref entity.GetComponent<TilePosition>();
            var miniDef = entity.HasComponent<MinifiedThing>()
                ? entity.GetComponent<MinifiedThing>().DefId : string.Empty;
            var snap = new ItemSnapshot(entity.Id, it.Kind, it.Count, it.Capacity, pos.TileX, pos.TileY, it.Forbidden, miniDef);
            var key = (pos.TileX, pos.TileY);
            if (!byTile.TryGetValue(key, out var list))
            {
                list = new List<ItemSnapshot>(1);
                byTile[key] = list;
            }
            list.Add(snap);
            byEntity[entity.Id] = snap;
        }
        return (byTile, byEntity);
    }

    // Stockpile tiles keyed to the union of kinds the underlying zone
    // accepts. A pickup at a tile is suppressed only when the tile's
    // stockpile accepts that item's kind — items that no longer match
    // their stockpile's filter become haulable so they can move out.
    private Dictionary<(int, int), ulong> CollectStockpileTiles()
    {
        var tiles = new Dictionary<(int, int), ulong>();
        foreach (var entity in _world.Store.Query<Zone>().Entities)
        {
            ref var z = ref entity.GetComponent<Zone>();
            if (z.Type != ZoneType.Stockpile) continue;
            var mask = entity.HasComponent<StockpileSettings>()
                ? entity.GetComponent<StockpileSettings>().AllowedKindsMask
                : StockpileFilter.DefaultMask;
            for (var ty = z.Rect.MinY; ty <= z.Rect.MaxY; ty++)
            {
                for (var tx = z.Rect.MinX; tx <= z.Rect.MaxX; tx++)
                {
                    if (!z.ContainsTile(tx, ty)) continue;
                    var key = (tx, ty);
                    tiles.TryGetValue(key, out var existing);
                    tiles[key] = existing | mask;
                }
            }
        }
        return tiles;
    }
}
