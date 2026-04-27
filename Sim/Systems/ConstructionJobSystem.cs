using CowColonySim.Sim.Blueprints;
using CowColonySim.Sim.Designations;
using CowColonySim.Sim.Items;
using CowColonySim.Sim.Pathfinding;
using CowColonySim.Sim.World;
using CowColonySim.Sim.World.Components;
using Friflo.Engine.ECS;

namespace CowColonySim.Sim.Systems;

// Drives wall construction: idle colonists pick up wood from anywhere
// (stockpile or ground), walk it to a hungry blueprint, deposit, and
// once the blueprint has all its material another colonist sits on the
// tile and ticks BuildProgress to 1.0. Completion deletes the ghost,
// spawns a Structure entity, and marks the tile blocked.
//
// MVP scope: 1×1 footprints only. Single material per def (Wood). One
// hauler/builder per blueprint at a time. Friflo archetype mutations
// are deferred outside the entity foreach.
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
            ref var job = ref entity.GetComponent<Job>();
            if (job.Active) continue;
            ref var work = ref entity.GetComponent<WorkJob>();
            ref var pos = ref entity.GetComponent<TilePosition>();
            ref var pf = ref entity.GetComponent<PathFollower>();

            if (work.Active && work.Kind == WorkKind.HaulToBlueprint)
            {
                ProgressHaul(entity, ref work, ref pf, ref pos, itemsByEntity, blueprints);
            }
            else if (work.Active && work.Kind == WorkKind.Construct)
            {
                ProgressConstruct(entity, ref work, ref pf, ref pos);
            }
            else if (!work.Active)
            {
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
            _world.AddOrMergeItem(d.TileX, d.TileY, d.Kind, d.Count);
            return;
        }
        ref var g = ref bpEnt.GetComponent<BlueprintGhost>();
        var def = BlueprintCatalog.Get(g.DefId);
        var required = TotalMaterialOf(def, d.Kind);
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
        Dictionary<int, ItemSnapshot> itemsByEntity, List<BlueprintSnapshot> blueprints)
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
                if (pf.LastPathFailed) { ClearWork(ref work, ref pf); return; }
                EnsurePath(entity, ref pf, pos.TileX, pos.TileY, item.TileX, item.TileY);
                return;
            }
            work.Carrying = true;
            work.CarryKind = item.Kind;
            work.CarryCount = item.Count;
            _pickupsToDelete.Add(work.TargetEntityId);
            EnsurePath(entity, ref pf, pos.TileX, pos.TileY, work.DropTileX, work.DropTileY);
            return;
        }

        if (pos.TileX != work.DropTileX || pos.TileY != work.DropTileY)
        {
            if (pf.LastPathFailed)
            {
                _deposits.Add(new DepositAction(0, work.CarryKind, work.CarryCount, pos.TileX, pos.TileY));
                ClearWork(ref work, ref pf);
                return;
            }
            EnsurePath(entity, ref pf, pos.TileX, pos.TileY, work.DropTileX, work.DropTileY);
            return;
        }

        var bp = FindBlueprintAt(blueprints, work.DropTileX, work.DropTileY);
        var bpId = bp.HasValue ? bp.Value.EntityId : 0;
        _deposits.Add(new DepositAction(bpId, work.CarryKind, work.CarryCount, work.DropTileX, work.DropTileY));
        ClearWork(ref work, ref pf);
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
            var snap = new ItemSnapshot(entity.Id, it.Kind, it.Count, pos.TileX, pos.TileY, it.Forbidden);
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
        public ItemSnapshot(int id, ItemKind k, int c, int tx, int ty, bool forbidden)
        {
            EntityId = id; Kind = k; Count = c; TileX = tx; TileY = ty; Forbidden = forbidden;
        }
    }

    private readonly struct DepositAction
    {
        public readonly int BlueprintId;
        public readonly ItemKind Kind;
        public readonly int Count;
        public readonly int TileX;
        public readonly int TileY;
        public DepositAction(int bp, ItemKind k, int c, int tx, int ty)
        { BlueprintId = bp; Kind = k; Count = c; TileX = tx; TileY = ty; }
    }
}
