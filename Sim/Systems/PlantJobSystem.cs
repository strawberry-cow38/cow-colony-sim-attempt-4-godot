using CowColonySim.Sim.Designations;
using CowColonySim.Sim.Items;
using CowColonySim.Sim.Pathfinding;
using CowColonySim.Sim.Plants;
using CowColonySim.Sim.World;
using CowColonySim.Sim.World.Components;
using Friflo.Engine.ECS;

namespace CowColonySim.Sim.Systems;

// Handles two designators that target Plant entities:
//   - DesignationKind.CutPlant   → WorkKind.CutPlant
//     Targets ANY plant. Below 50% growth nothing drops; at/above 50%
//     drops the plant's CropDef yield (saplings get cleared, mature
//     plants get harvested ergonomically).
//   - DesignationKind.Harvest    → WorkKind.HarvestPlant
//     Targets non-tree plants only, and only at >=50% growth.
//     Always drops the CropDef yield. Trees ignore harvest by design;
//     ChopJobSystem owns trees.
//
// Stand policy: blocked tiles (mature trees) require adjacent stand;
// walkable tiles (most crops) let the colonist work the tile itself.
// Need-driven Job preempts as in ChopJobSystem — WorkJob is held but
// progress doesn't advance.
public sealed class PlantJobSystem : ITickSystem
{
    private const float WorkIntervalSec = 0.55f;

    private readonly SimWorld _world;
    private readonly PathPlanner _planner;
    private readonly HeightGrid _grid;

    public PlantJobSystem(SimWorld world, PathPlanner planner, HeightGrid grid)
    {
        _world = world;
        _planner = planner;
        _grid = grid;
    }

    private readonly List<FinishedPlant> _finished = new();

    private readonly struct FinishedPlant
    {
        public readonly int PlantId;
        public readonly int DesignationId;
        public readonly int TileX;
        public readonly int TileY;
        public readonly ItemKind YieldKind;
        public readonly int YieldCount;
        public readonly bool WasTree;
        public FinishedPlant(int plantId, int designationId, int tileX, int tileY,
            ItemKind yieldKind, int yieldCount, bool wasTree)
        {
            PlantId = plantId;
            DesignationId = designationId;
            TileX = tileX;
            TileY = tileY;
            YieldKind = yieldKind;
            YieldCount = yieldCount;
            WasTree = wasTree;
        }
    }

    public void Tick(TickContext ctx)
    {
        var dt = (float)ctx.FixedDeltaSeconds;
        var plants = CollectPlants();
        var cuts = CollectDesignations(DesignationKind.CutPlant);
        var harvests = CollectDesignations(DesignationKind.Harvest);

        var claimed = new HashSet<int>();
        var query = _world.Store.Query<Colonist, Job, WorkJob, TilePosition, PathFollower>();
        foreach (var entity in query.Entities)
        {
            ref var work = ref entity.GetComponent<WorkJob>();
            if (work.Active && (work.Kind == WorkKind.CutPlant || work.Kind == WorkKind.HarvestPlant))
                claimed.Add(work.TargetEntityId);
        }

        _finished.Clear();
        foreach (var entity in query.Entities)
        {
            if (entity.HasComponent<Drafted>() && entity.GetComponent<Drafted>().Active) continue;
            ref var job = ref entity.GetComponent<Job>();
            ref var work = ref entity.GetComponent<WorkJob>();
            ref var pos = ref entity.GetComponent<TilePosition>();
            ref var pf = ref entity.GetComponent<PathFollower>();

            if (job.Active) continue;

            if (work.Active && (work.Kind == WorkKind.CutPlant || work.Kind == WorkKind.HarvestPlant))
            {
                Progress(entity, ref work, ref pf, ref pos, plants, cuts, harvests, dt);
            }
            else if (!work.Active)
            {
                if (entity.HasComponent<WorkPriorities>() &&
                    entity.GetComponent<WorkPriorities>().Get(WorkType.Plants) == 0) continue;
                TryAssign(entity, ref work, ref pf, ref pos, plants, cuts, harvests, claimed);
            }
        }

        // Apply structural changes outside the foreach. Same dedupe rule as
        // ChopJobSystem: two colonists could push the same plant past 0
        // health on the same tick.
        var seen = new HashSet<int>();
        for (var i = 0; i < _finished.Count; i++)
        {
            var f = _finished[i];
            if (!seen.Add(f.PlantId)) continue;
            if (f.WasTree) _grid.MarkBlocked(f.TileX, f.TileY, false);
            if (f.YieldCount > 0 && f.YieldKind != ItemKind.None)
            {
                _world.AddOrMergeItem(f.TileX, f.TileY, f.YieldKind, f.YieldCount);
            }
            var plant = _world.Store.GetEntityById(f.PlantId);
            if (plant != default) plant.DeleteEntity();
            var designation = _world.Store.GetEntityById(f.DesignationId);
            if (designation != default) designation.DeleteEntity();
            if (f.WasTree) _world.RecordTreeFall(f.TileX, f.TileY);
        }
    }

    private void Progress(
        Entity entity, ref WorkJob work, ref PathFollower pf, ref TilePosition pos,
        Dictionary<(int, int), int> plants,
        Dictionary<(int, int), int> cuts,
        Dictionary<(int, int), int> harvests,
        float dt)
    {
        var key = (work.TargetTileX, work.TargetTileY);
        if (!plants.TryGetValue(key, out var plantId) || plantId != work.TargetEntityId)
        {
            ClearWork(ref work, ref pf);
            return;
        }
        var designations = work.Kind == WorkKind.CutPlant ? cuts : harvests;
        if (!designations.TryGetValue(key, out var designationId))
        {
            ClearWork(ref work, ref pf);
            return;
        }

        var blocked = _grid.IsBlocked(work.TargetTileX, work.TargetTileY);
        if (!IsWithinReach(pos.TileX, pos.TileY, work.TargetTileX, work.TargetTileY, blocked))
        {
            if (pf.Tiles is null && !pf.PendingRequest)
            {
                if (TryFindStandTile(work.TargetTileX, work.TargetTileY, pos.TileX, pos.TileY, blocked, out var stand))
                {
                    var start = _grid.At(
                        Math.Clamp(pos.TileX, 0, _grid.Width - 1),
                        Math.Clamp(pos.TileY, 0, _grid.Height - 1));
                    if (start != stand)
                    {
                        pf.PendingRequest = true;
                        pf.PlayerForced = false;
                        _planner.Request(entity.Id, start, stand);
                    }
                }
                else
                {
                    ClearWork(ref work, ref pf);
                }
            }
            return;
        }

        var plant = _world.Store.GetEntityById(plantId);
        if (plant == default || !plant.HasComponent<Plant>())
        {
            ClearWork(ref work, ref pf);
            return;
        }
        ref var p = ref plant.GetComponent<Plant>();
        work.Progress += dt;
        if (work.Progress < WorkIntervalSec) return;
        work.Progress -= WorkIntervalSec;

        // One tick of work finishes the plant — crops are one-shot. Trees
        // being cut also fall in one tick of work to keep cut snappy; the
        // chop loop is the slow path for mature trees.
        var def = CropCatalog.Get(p.CropDefId);
        var yieldCount = p.Growth >= 50f ? def.YieldCount : 0;
        var yieldKind = yieldCount > 0 ? def.YieldItemKind : ItemKind.None;
        _finished.Add(new FinishedPlant(
            plantId, designationId, work.TargetTileX, work.TargetTileY,
            yieldKind, yieldCount, p.IsTree));
        ClearWork(ref work, ref pf);
    }

    private void TryAssign(
        Entity entity, ref WorkJob work, ref PathFollower pf, ref TilePosition pos,
        Dictionary<(int, int), int> plants,
        Dictionary<(int, int), int> cuts,
        Dictionary<(int, int), int> harvests,
        HashSet<int> claimed)
    {
        var bestPlantId = 0;
        var bestKey = (0, 0);
        var bestKind = WorkKind.None;
        var bestDistSq = float.PositiveInfinity;

        foreach (var key in cuts.Keys)
        {
            if (!plants.TryGetValue(key, out var plantId)) continue;
            if (claimed.Contains(plantId)) continue;
            var dx = key.Item1 - pos.TileX;
            var dy = key.Item2 - pos.TileY;
            var d = dx * dx + dy * dy;
            if (d >= bestDistSq) continue;
            bestDistSq = d;
            bestKey = key;
            bestPlantId = plantId;
            bestKind = WorkKind.CutPlant;
        }

        foreach (var key in harvests.Keys)
        {
            if (!plants.TryGetValue(key, out var plantId)) continue;
            if (claimed.Contains(plantId)) continue;
            var plant = _world.Store.GetEntityById(plantId);
            if (plant == default) continue;
            ref var pc = ref plant.GetComponent<Plant>();
            // Harvest only fires on mature non-tree plants; trees ignore
            // the harvest designator.
            if (pc.IsTree) continue;
            if (pc.Growth < 50f) continue;
            var dx = key.Item1 - pos.TileX;
            var dy = key.Item2 - pos.TileY;
            var d = dx * dx + dy * dy;
            if (d >= bestDistSq) continue;
            bestDistSq = d;
            bestKey = key;
            bestPlantId = plantId;
            bestKind = WorkKind.HarvestPlant;
        }

        if (bestPlantId == 0) return;

        work.Active = true;
        work.Kind = bestKind;
        work.TargetTileX = bestKey.Item1;
        work.TargetTileY = bestKey.Item2;
        work.TargetEntityId = bestPlantId;
        work.Progress = 0f;
        claimed.Add(bestPlantId);

        var blocked = _grid.IsBlocked(bestKey.Item1, bestKey.Item2);
        if (IsWithinReach(pos.TileX, pos.TileY, bestKey.Item1, bestKey.Item2, blocked)) return;
        if (!TryFindStandTile(bestKey.Item1, bestKey.Item2, pos.TileX, pos.TileY, blocked, out var stand))
        {
            ClearWork(ref work, ref pf);
            return;
        }
        var start = _grid.At(
            Math.Clamp(pos.TileX, 0, _grid.Width - 1),
            Math.Clamp(pos.TileY, 0, _grid.Height - 1));
        if (start == stand) return;
        pf.Tiles = null;
        pf.Index = 0;
        pf.PendingRequest = true;
        pf.PlayerForced = false;
        _planner.Request(entity.Id, start, stand);
    }

    // For walkable plant tiles (most crops) the colonist can stand right
    // on the tile; for blocked tiles (mature trees) they must be next door.
    private static bool IsWithinReach(int ax, int ay, int bx, int by, bool blockedTarget)
    {
        if (!blockedTarget && ax == bx && ay == by) return true;
        return Math.Abs(ax - bx) <= 1 && Math.Abs(ay - by) <= 1;
    }

    private bool TryFindStandTile(int targetX, int targetY, int fromX, int fromY, bool blockedTarget, out TileCoord stand)
    {
        if (!blockedTarget
            && (uint)targetX < (uint)_grid.Width
            && (uint)targetY < (uint)_grid.Height)
        {
            stand = _grid.At(targetX, targetY);
            return true;
        }

        var bestDistSq = int.MaxValue;
        var bestX = 0;
        var bestY = 0;
        var found = false;
        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                var nx = targetX + dx;
                var ny = targetY + dy;
                if ((uint)nx >= (uint)_grid.Width || (uint)ny >= (uint)_grid.Height) continue;
                if (_grid.IsBlocked(nx, ny)) continue;
                var ddx = nx - fromX;
                var ddy = ny - fromY;
                var d = ddx * ddx + ddy * ddy;
                if (d < bestDistSq)
                {
                    bestDistSq = d;
                    bestX = nx;
                    bestY = ny;
                    found = true;
                }
            }
        }
        stand = found ? _grid.At(bestX, bestY) : default;
        return found;
    }

    private static void ClearWork(ref WorkJob work, ref PathFollower pf)
    {
        work.Active = false;
        work.Kind = WorkKind.None;
        work.TargetEntityId = 0;
        work.Progress = 0f;
        work.Forced = false;
        pf.Tiles = null;
        pf.Index = 0;
    }

    private Dictionary<(int, int), int> CollectPlants()
    {
        var query = _world.Store.Query<Plant, TilePosition>();
        var result = new Dictionary<(int, int), int>(query.Count);
        foreach (var entity in query.Entities)
        {
            ref var pos = ref entity.GetComponent<TilePosition>();
            result[(pos.TileX, pos.TileY)] = entity.Id;
        }
        return result;
    }

    private Dictionary<(int, int), int> CollectDesignations(DesignationKind kind)
    {
        var query = _world.Store.Query<Designation, TilePosition>();
        var result = new Dictionary<(int, int), int>(query.Count);
        foreach (var entity in query.Entities)
        {
            ref var d = ref entity.GetComponent<Designation>();
            if (d.Kind != kind) continue;
            ref var pos = ref entity.GetComponent<TilePosition>();
            result[(pos.TileX, pos.TileY)] = entity.Id;
        }
        return result;
    }
}
