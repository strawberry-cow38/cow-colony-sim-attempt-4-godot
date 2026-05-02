using CowColonySim.Sim.Blueprints;
using CowColonySim.Sim.Designations;
using CowColonySim.Sim.Pathfinding;
using CowColonySim.Sim.World;
using CowColonySim.Sim.World.Components;
using Friflo.Engine.ECS;

namespace CowColonySim.Sim.Systems;

// Picks Uninstall + Deconstruct designations off the world and walks
// idle colonists to the matching Structure. On arrival, ticks Progress
// up to WorkSeconds; on completion swaps the structure to a minified
// thing (uninstall) or refunds half the materials (deconstruct), then
// clears the designation.
//
// Mirrors ChopJobSystem's adjacency + stand-tile logic so the colonist
// stops next to the structure instead of trying to walk onto a blocked
// wall tile.
public sealed class StructureWorkSystem : ITickSystem
{
    // Flat work duration for now — Phase-3 will scale by skill, structure
    // size, and material weight.
    private const float WorkSeconds = 3f;

    private readonly SimWorld _world;
    private readonly PathPlanner _planner;
    private readonly HeightGrid _grid;

    private readonly List<Completion> _completions = new();

    private readonly struct Completion
    {
        public readonly int StructureId;
        public readonly int DesignationId;
        public readonly DesignationKind Kind;
        public Completion(int sid, int did, DesignationKind k)
        {
            StructureId = sid; DesignationId = did; Kind = k;
        }
    }

    public StructureWorkSystem(SimWorld world, PathPlanner planner, HeightGrid grid)
    {
        _world = world;
        _planner = planner;
        _grid = grid;
    }

    public void Tick(TickContext ctx)
    {
        var dt = (float)ctx.FixedDeltaSeconds;
        var jobs = CollectJobs();

        var claimed = new HashSet<int>();
        var query = _world.Store.Query<Colonist, Job, WorkJob, TilePosition, PathFollower>();
        foreach (var entity in query.Entities)
        {
            ref var work = ref entity.GetComponent<WorkJob>();
            if (!work.Active) continue;
            if (work.Kind == WorkKind.Uninstall || work.Kind == WorkKind.Deconstruct)
                claimed.Add(work.TargetEntityId);
        }

        _completions.Clear();
        foreach (var entity in query.Entities)
        {
            if (entity.HasComponent<Drafted>() && entity.GetComponent<Drafted>().Active) continue;
            ref var job = ref entity.GetComponent<Job>();
            ref var work = ref entity.GetComponent<WorkJob>();
            ref var pos = ref entity.GetComponent<TilePosition>();
            ref var pf = ref entity.GetComponent<PathFollower>();

            if (job.Active) continue;

            if (work.Active && (work.Kind == WorkKind.Uninstall || work.Kind == WorkKind.Deconstruct))
            {
                ProgressWork(entity, ref work, ref pf, ref pos, jobs, dt);
            }
            else if (!work.Active)
            {
                if (entity.HasComponent<WorkPriorities>() &&
                    entity.GetComponent<WorkPriorities>().Get(WorkType.StructureWork) == 0) continue;
                TryAssign(entity, ref work, ref pf, ref pos, jobs, claimed);
            }
        }

        // Apply structural changes outside the colonist loop so deletes
        // don't invalidate the iterator. Dedupe by structure id in case
        // two colonists land the final tick simultaneously.
        var seen = new HashSet<int>();
        for (var i = 0; i < _completions.Count; i++)
        {
            var c = _completions[i];
            if (!seen.Add(c.StructureId)) continue;
            CompleteOne(c);
        }
    }

    private void CompleteOne(Completion c)
    {
        var ent = _world.Store.GetEntityById(c.StructureId);
        if (ent != default && ent.HasComponent<Structure>() && ent.HasComponent<TilePosition>())
        {
            ref var s = ref ent.GetComponent<Structure>();
            ref var pos = ref ent.GetComponent<TilePosition>();
            var defId = s.DefId;
            var rotation = s.Rotation;
            var baseLayer = s.BaseLayer;
            var tx = pos.TileX;
            var ty = pos.TileY;
            var def = BlueprintCatalog.Get(defId);
            UnblockFootprint(def, rotation, tx, ty, baseLayer);

            // Power-bearing structure leaves stale grid edges + cached grid
            // membership behind if topology rebuild isn't kicked.
            if (ent.HasComponent<PowerNode>()) _world.BumpPowerVersion();

            ent.DeleteEntity();
            if (c.Kind == DesignationKind.Uninstall)
            {
                _world.SpawnMinifiedThing(defId, tx, ty, rotation, baseLayer);
            }
            else
            {
                var mats = def.MaterialsOrEmpty;
                for (var i = 0; i < mats.Count; i++)
                {
                    var m = mats[i];
                    var refund = m.Count / 2;
                    if (refund > 0) _world.AddOrMergeItem(tx, ty, m.Kind, refund);
                }
            }
        }
        var d = _world.Store.GetEntityById(c.DesignationId);
        if (d != default) d.DeleteEntity();
    }

    private void ProgressWork(
        Entity entity, ref WorkJob work, ref PathFollower pf, ref TilePosition pos,
        Dictionary<int, JobInfo> jobs, float dt)
    {
        if (!jobs.TryGetValue(work.TargetEntityId, out var info))
        {
            ClearWork(ref work, ref pf);
            return;
        }
        // Designation kind may have changed under the colonist (rare) —
        // only progress if it still matches the work kind.
        var matches = (work.Kind == WorkKind.Uninstall && info.Kind == DesignationKind.Uninstall)
                   || (work.Kind == WorkKind.Deconstruct && info.Kind == DesignationKind.Deconstruct);
        if (!matches)
        {
            ClearWork(ref work, ref pf);
            return;
        }

        if (!IsAdjacent(pos.TileX, pos.TileY, info.TileX, info.TileY))
        {
            if (pf.LastPathFailed)
            {
                pf.LastPathFailed = false;
                _world.UnreachableWorkTargets.Add(work.TargetEntityId);
                ClearWork(ref work, ref pf);
                return;
            }
            if (pf.Tiles is null && !pf.PendingRequest)
            {
                if (TryFindStandTile(info.TileX, info.TileY, pos.TileX, pos.TileY, out var stand))
                {
                    var start = _grid.NodeAtOrFloor(pos.TileX, pos.TileY, pos.TileZ);
                    if (start != stand)
                    {
                        pf.PendingRequest = true;
                        pf.PlayerForced = false;
                        _planner.Request(entity.Id, start, stand);
                    }
                }
                else
                {
                    _world.UnreachableWorkTargets.Add(work.TargetEntityId);
                    ClearWork(ref work, ref pf);
                }
            }
            return;
        }

        work.Progress += dt;
        if (work.Progress >= WorkSeconds)
        {
            _completions.Add(new Completion(info.StructureId, info.DesignationId, info.Kind));
            ClearWork(ref work, ref pf);
        }
    }

    private void TryAssign(
        Entity entity, ref WorkJob work, ref PathFollower pf, ref TilePosition pos,
        Dictionary<int, JobInfo> jobs, HashSet<int> claimed)
    {
        var bestId = 0;
        var bestKind = DesignationKind.Uninstall;
        var bestX = 0;
        var bestY = 0;
        var bestDistSq = float.PositiveInfinity;
        foreach (var kv in jobs)
        {
            var j = kv.Value;
            if (claimed.Contains(j.StructureId)) continue;
            if (_world.UnreachableWorkTargets.Contains(j.StructureId)) continue;
            var dx = j.TileX - pos.TileX;
            var dy = j.TileY - pos.TileY;
            var d = dx * dx + dy * dy;
            if (d < bestDistSq)
            {
                bestDistSq = d;
                bestId = j.StructureId;
                bestKind = j.Kind;
                bestX = j.TileX;
                bestY = j.TileY;
            }
        }
        if (bestId == 0) return;

        work.Active = true;
        work.Kind = bestKind == DesignationKind.Uninstall ? WorkKind.Uninstall : WorkKind.Deconstruct;
        work.TargetEntityId = bestId;
        work.TargetTileX = bestX;
        work.TargetTileY = bestY;
        work.Progress = 0f;
        claimed.Add(bestId);

        if (IsAdjacent(pos.TileX, pos.TileY, bestX, bestY)) return;
        if (!TryFindStandTile(bestX, bestY, pos.TileX, pos.TileY, out var stand))
        {
            ClearWork(ref work, ref pf);
            return;
        }
        var start = _grid.NodeAtOrFloor(pos.TileX, pos.TileY, pos.TileZ);
        if (start == stand) return;
        pf.Tiles = null;
        pf.Index = 0;
        pf.PendingRequest = true;
        pf.PlayerForced = false;
        _planner.Request(entity.Id, start, stand);
    }

    private static bool IsAdjacent(int ax, int ay, int bx, int by) =>
        Math.Abs(ax - bx) <= 1 && Math.Abs(ay - by) <= 1;

    private bool TryFindStandTile(int targetX, int targetY, int fromX, int fromY, out TileCoord stand)
    {
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

    private void UnblockFootprint(BlueprintDef def, int rotation, int tileX, int tileY, int baseLayer)
    {
        if (def.Category != BlueprintCategory.Structure) return;
        var (footW, footH) = (rotation & 1) == 0 ? (def.FootprintW, def.FootprintH) : (def.FootprintH, def.FootprintW);
        for (var dy = 0; dy < footH; dy++)
        {
            for (var dx = 0; dx < footW; dx++)
            {
                HeightGridOps.UnregisterStructure(_grid, def, tileX + dx, tileY + dy, baseLayer);
            }
        }
        _world.ClearUnreachableWorkTargets();
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

    private readonly struct JobInfo
    {
        public readonly int StructureId;
        public readonly int DesignationId;
        public readonly DesignationKind Kind;
        public readonly int TileX;
        public readonly int TileY;
        public JobInfo(int sid, int did, DesignationKind k, int tx, int ty)
        { StructureId = sid; DesignationId = did; Kind = k; TileX = tx; TileY = ty; }
    }

    // Pair each Uninstall/Deconstruct designation with the structure
    // sitting on its tile. Designations without a structure under them
    // (cancelled mid-flight, never had one) get skipped.
    private Dictionary<int, JobInfo> CollectJobs()
    {
        var structuresByTile = new Dictionary<(int, int), int>();
        foreach (var ent in _world.Store.Query<Structure, TilePosition>().Entities)
        {
            ref var pos = ref ent.GetComponent<TilePosition>();
            structuresByTile[(pos.TileX, pos.TileY)] = ent.Id;
        }

        var jobs = new Dictionary<int, JobInfo>();
        foreach (var ent in _world.Store.Query<Designation, TilePosition>().Entities)
        {
            ref var d = ref ent.GetComponent<Designation>();
            if (d.Kind != DesignationKind.Uninstall && d.Kind != DesignationKind.Deconstruct) continue;
            ref var pos = ref ent.GetComponent<TilePosition>();
            if (!structuresByTile.TryGetValue((pos.TileX, pos.TileY), out var sid)) continue;
            jobs[sid] = new JobInfo(sid, ent.Id, d.Kind, pos.TileX, pos.TileY);
        }
        return jobs;
    }
}
