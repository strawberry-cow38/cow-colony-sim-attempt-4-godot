using CowColonySim.Sim.Designations;
using CowColonySim.Sim.Items;
using CowColonySim.Sim.Pathfinding;
using CowColonySim.Sim.World;
using CowColonySim.Sim.World.Components;
using CowColonySim.Sim.Zones;
using Friflo.Engine.ECS;

namespace CowColonySim.Sim.Systems;

// Auto-haul: idle colonists pick up non-forbidden item stacks that
// aren't already inside a stockpile and walk them to the highest-
// priority stockpile zone with room. Two-phase WorkJob — phase 0 walks
// to the source item, phase 1 walks to the drop tile. Carrying flips
// when the colonist reaches the pickup tile (item entity consumed,
// payload buffered into WorkJob until deposit).
//
// Like ChopJobSystem, all entity creation/deletion is deferred outside
// the colonist iteration. Friflo crashes if you mutate archetype
// storage mid-foreach.
public sealed class HaulSystem : ITickSystem
{
    private readonly SimWorld _world;
    private readonly PathPlanner _planner;
    private readonly HeightGrid _grid;

    private readonly List<PickupAction> _pickups = new();
    private readonly List<DepositAction> _deposits = new();

    private readonly struct PickupAction
    {
        public readonly int ItemEntityId;
        public PickupAction(int itemEntityId) { ItemEntityId = itemEntityId; }
    }

    private readonly struct DepositAction
    {
        public readonly int TileX;
        public readonly int TileY;
        public readonly ItemKind Kind;
        public readonly int Count;
        public DepositAction(int tx, int ty, ItemKind kind, int count)
        { TileX = tx; TileY = ty; Kind = kind; Count = count; }
    }

    public HaulSystem(SimWorld world, PathPlanner planner, HeightGrid grid)
    {
        _world = world;
        _planner = planner;
        _grid = grid;
    }

    public void Tick(TickContext ctx)
    {
        _pickups.Clear();
        _deposits.Clear();

        var stockpileTiles = CollectStockpileTiles();
        var (itemsByTile, itemsByEntity) = CollectItems();

        var claimedItems = new HashSet<int>();
        var occupiedDropTiles = new HashSet<(int, int)>();
        var query = _world.Store.Query<Colonist, Job, WorkJob, TilePosition, PathFollower>();
        foreach (var entity in query.Entities)
        {
            ref var work = ref entity.GetComponent<WorkJob>();
            if (!work.Active || work.Kind != WorkKind.HaulItem) continue;
            claimedItems.Add(work.TargetEntityId);
            occupiedDropTiles.Add((work.DropTileX, work.DropTileY));
        }

        foreach (var entity in query.Entities)
        {
            ref var job = ref entity.GetComponent<Job>();
            ref var work = ref entity.GetComponent<WorkJob>();
            ref var pos = ref entity.GetComponent<TilePosition>();
            ref var pf = ref entity.GetComponent<PathFollower>();

            if (job.Active) continue;

            if (work.Active && work.Kind == WorkKind.HaulItem)
            {
                ProgressHaul(entity, ref work, ref pf, ref pos, itemsByEntity, stockpileTiles);
            }
            else if (!work.Active)
            {
                TryAssignHaul(entity, ref work, ref pf, ref pos, itemsByTile, stockpileTiles, claimedItems, occupiedDropTiles);
            }
        }

        for (var i = 0; i < _pickups.Count; i++)
        {
            var p = _pickups[i];
            var item = _world.Store.GetEntityById(p.ItemEntityId);
            if (item != default) item.DeleteEntity();
        }
        for (var i = 0; i < _deposits.Count; i++)
        {
            var d = _deposits[i];
            _world.AddOrMergeItem(d.TileX, d.TileY, d.Kind, d.Count);
        }
    }

    private void ProgressHaul(
        Entity entity, ref WorkJob work, ref PathFollower pf, ref TilePosition pos,
        Dictionary<int, ItemSnapshot> itemsByEntity, HashSet<(int, int)> stockpileTiles)
    {
        if (!work.Carrying)
        {
            if (!itemsByEntity.TryGetValue(work.TargetEntityId, out var item) || item.Forbidden)
            {
                ClearWork(ref work, ref pf);
                return;
            }
            if (pos.TileX != item.TileX || pos.TileY != item.TileY)
            {
                if (pf.LastPathFailed)
                {
                    ClearWork(ref work, ref pf);
                    return;
                }
                EnsurePath(entity, ref pf, pos.TileX, pos.TileY, item.TileX, item.TileY);
                return;
            }
            // Reached pickup tile. Buffer payload, defer the actual entity
            // delete to post-loop.
            work.Carrying = true;
            work.CarryKind = item.Kind;
            work.CarryCount = item.Count;
            _pickups.Add(new PickupAction(work.TargetEntityId));
            // Switch the goal to the stockpile drop tile.
            EnsurePath(entity, ref pf, pos.TileX, pos.TileY, work.DropTileX, work.DropTileY);
            return;
        }

        if (pos.TileX != work.DropTileX || pos.TileY != work.DropTileY)
        {
            // Drop tile unreachable (stockpile fenced in, etc). Don't loop
            // forever re-requesting — drop the carry at the colonist's
            // current tile so it lives somewhere visible.
            if (pf.LastPathFailed)
            {
                _deposits.Add(new DepositAction(pos.TileX, pos.TileY, work.CarryKind, work.CarryCount));
                ClearWork(ref work, ref pf);
                return;
            }
            EnsurePath(entity, ref pf, pos.TileX, pos.TileY, work.DropTileX, work.DropTileY);
            return;
        }
        _deposits.Add(new DepositAction(work.DropTileX, work.DropTileY, work.CarryKind, work.CarryCount));
        ClearWork(ref work, ref pf);
    }

    private void TryAssignHaul(
        Entity entity, ref WorkJob work, ref PathFollower pf, ref TilePosition pos,
        Dictionary<(int, int), ItemSnapshot> itemsByTile, HashSet<(int, int)> stockpileTiles,
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
            var item = kv.Value;
            if (item.Forbidden) continue;
            if (claimedItems.Contains(item.EntityId)) continue;
            if (stockpileTiles.Contains((item.TileX, item.TileY))) continue;
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
        if (bestItem == -1) return;

        if (!TryFindDropTile(bestKind, bestCount, itemsByTile, occupiedDropTiles, out var dropX, out var dropY)) return;

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
        claimedItems.Add(bestItem);
        occupiedDropTiles.Add((dropX, dropY));

        EnsurePath(entity, ref pf, pos.TileX, pos.TileY, bestItemTileX, bestItemTileY);
    }

    private bool TryFindDropTile(
        ItemKind kind, int count,
        Dictionary<(int, int), ItemSnapshot> itemsByTile,
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
            var priority = entity.HasComponent<StockpileSettings>()
                ? entity.GetComponent<StockpileSettings>().Priority : 0;
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
                        if (existing.Kind != kind) continue;
                        var room = existing.Capacity - existing.Count;
                        if (room <= 0) continue;
                        partial = existing.Count;
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
        public ItemSnapshot(int id, ItemKind kind, int count, int capacity, int tx, int ty, bool forbidden)
        {
            EntityId = id;
            Kind = kind;
            Count = count;
            Capacity = capacity;
            TileX = tx;
            TileY = ty;
            Forbidden = forbidden;
        }
    }

    private (Dictionary<(int, int), ItemSnapshot>, Dictionary<int, ItemSnapshot>) CollectItems()
    {
        var byTile = new Dictionary<(int, int), ItemSnapshot>();
        var byEntity = new Dictionary<int, ItemSnapshot>();
        foreach (var entity in _world.Store.Query<Item, TilePosition>().Entities)
        {
            ref var it = ref entity.GetComponent<Item>();
            ref var pos = ref entity.GetComponent<TilePosition>();
            var snap = new ItemSnapshot(entity.Id, it.Kind, it.Count, it.Capacity, pos.TileX, pos.TileY, it.Forbidden);
            byTile[(pos.TileX, pos.TileY)] = snap;
            byEntity[entity.Id] = snap;
        }
        return (byTile, byEntity);
    }

    private HashSet<(int, int)> CollectStockpileTiles()
    {
        var tiles = new HashSet<(int, int)>();
        foreach (var entity in _world.Store.Query<Zone>().Entities)
        {
            ref var z = ref entity.GetComponent<Zone>();
            if (z.Type != ZoneType.Stockpile) continue;
            for (var ty = z.Rect.MinY; ty <= z.Rect.MaxY; ty++)
            {
                for (var tx = z.Rect.MinX; tx <= z.Rect.MaxX; tx++)
                {
                    if (z.ContainsTile(tx, ty)) tiles.Add((tx, ty));
                }
            }
        }
        return tiles;
    }
}
