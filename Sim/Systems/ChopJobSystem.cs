using CowColonySim.Sim.Designations;
using CowColonySim.Sim.Pathfinding;
using CowColonySim.Sim.World;
using CowColonySim.Sim.World.Components;
using Friflo.Engine.ECS;

namespace CowColonySim.Sim.Systems;

// Picks ChopTree designations off the world and walks idle colonists to
// the matching Tree. When a colonist arrives, Health drains at ChopRate
// per second. When Health hits zero the Tree + Designation entities are
// deleted and the WorkJob clears.
//
// Need-driven Job always preempts: if Job.Active, the colonist's WorkJob
// stays Active but doesn't progress — when needs settle it resumes.
public sealed class ChopJobSystem : ITickSystem
{
    private const float ChopRatePerSec = 2f;

    private readonly SimWorld _world;
    private readonly PathPlanner _planner;
    private readonly HeightGrid _grid;

    public ChopJobSystem(SimWorld world, PathPlanner planner, HeightGrid grid)
    {
        _world = world;
        _planner = planner;
        _grid = grid;
    }

    public void Tick(TickContext ctx)
    {
        var dt = (float)ctx.FixedDeltaSeconds;
        var trees = CollectTrees();
        var chopDesignations = CollectChopDesignations();

        var claimedTrees = new HashSet<int>();
        var query = _world.Store.Query<Colonist, Job, WorkJob, TilePosition, PathFollower>();
        foreach (var entity in query.Entities)
        {
            ref var work = ref entity.GetComponent<WorkJob>();
            if (work.Active) claimedTrees.Add(work.TargetEntityId);
        }

        foreach (var entity in query.Entities)
        {
            ref var job = ref entity.GetComponent<Job>();
            ref var work = ref entity.GetComponent<WorkJob>();
            ref var pos = ref entity.GetComponent<TilePosition>();
            ref var pf = ref entity.GetComponent<PathFollower>();

            if (job.Active) continue;

            if (work.Active)
            {
                ProgressChop(entity, ref work, ref pf, ref pos, trees, chopDesignations, dt);
            }
            else
            {
                TryAssignChop(entity, ref work, ref pf, ref pos, trees, chopDesignations, claimedTrees);
            }
        }
    }

    private void ProgressChop(
        Entity entity, ref WorkJob work, ref PathFollower pf, ref TilePosition pos,
        Dictionary<(int, int), int> trees, Dictionary<(int, int), int> chops, float dt)
    {
        var key = (work.TargetTileX, work.TargetTileY);
        if (!trees.TryGetValue(key, out var treeId) || treeId != work.TargetEntityId)
        {
            ClearWork(ref work, ref pf);
            return;
        }
        if (!chops.TryGetValue(key, out var designationId))
        {
            ClearWork(ref work, ref pf);
            return;
        }

        if (pos.TileX != work.TargetTileX || pos.TileY != work.TargetTileY)
        {
            if (pf.Tiles is null && !pf.PendingRequest)
            {
                var start = _grid.At(
                    Math.Clamp(pos.TileX, 0, _grid.Width - 1),
                    Math.Clamp(pos.TileY, 0, _grid.Height - 1));
                var goal = _grid.At(
                    Math.Clamp(work.TargetTileX, 0, _grid.Width - 1),
                    Math.Clamp(work.TargetTileY, 0, _grid.Height - 1));
                if (start != goal)
                {
                    pf.PendingRequest = true;
                    pf.PlayerForced = false;
                    _planner.Request(entity.Id, start, goal);
                }
            }
            return;
        }

        var tree = _world.Store.GetEntityById(treeId);
        if (tree == default || !tree.HasComponent<Tree>())
        {
            ClearWork(ref work, ref pf);
            return;
        }
        ref var t = ref tree.GetComponent<Tree>();
        work.Progress += ChopRatePerSec * dt;
        var whole = (int)work.Progress;
        if (whole > 0)
        {
            work.Progress -= whole;
            t.Health -= whole;
        }
        if (t.Health <= 0)
        {
            tree.DeleteEntity();
            var designation = _world.Store.GetEntityById(designationId);
            if (designation != default) designation.DeleteEntity();
            ClearWork(ref work, ref pf);
        }
    }

    private void TryAssignChop(
        Entity entity, ref WorkJob work, ref PathFollower pf, ref TilePosition pos,
        Dictionary<(int, int), int> trees, Dictionary<(int, int), int> chops,
        HashSet<int> claimedTrees)
    {
        var bestTreeId = 0;
        var bestKey = (0, 0);
        var bestDistSq = float.PositiveInfinity;
        foreach (var key in chops.Keys)
        {
            if (!trees.TryGetValue(key, out var treeId)) continue;
            if (claimedTrees.Contains(treeId)) continue;
            var dx = key.Item1 - pos.TileX;
            var dy = key.Item2 - pos.TileY;
            var d = dx * dx + dy * dy;
            if (d < bestDistSq)
            {
                bestDistSq = d;
                bestKey = key;
                bestTreeId = treeId;
            }
        }
        if (bestTreeId == 0) return;

        work.Active = true;
        work.Kind = WorkKind.ChopTree;
        work.TargetTileX = bestKey.Item1;
        work.TargetTileY = bestKey.Item2;
        work.TargetEntityId = bestTreeId;
        work.Progress = 0f;
        claimedTrees.Add(bestTreeId);

        if (pos.TileX == bestKey.Item1 && pos.TileY == bestKey.Item2) return;

        var start = _grid.At(
            Math.Clamp(pos.TileX, 0, _grid.Width - 1),
            Math.Clamp(pos.TileY, 0, _grid.Height - 1));
        var goal = _grid.At(
            Math.Clamp(bestKey.Item1, 0, _grid.Width - 1),
            Math.Clamp(bestKey.Item2, 0, _grid.Height - 1));
        if (start == goal) return;
        pf.Tiles = null;
        pf.Index = 0;
        pf.PendingRequest = true;
        pf.PlayerForced = false;
        _planner.Request(entity.Id, start, goal);
    }

    private static void ClearWork(ref WorkJob work, ref PathFollower pf)
    {
        work.Active = false;
        work.Kind = WorkKind.None;
        work.TargetEntityId = 0;
        work.Progress = 0f;
        pf.Tiles = null;
        pf.Index = 0;
    }

    private Dictionary<(int, int), int> CollectTrees()
    {
        var query = _world.Store.Query<Tree, TilePosition>();
        var result = new Dictionary<(int, int), int>(query.Count);
        foreach (var entity in query.Entities)
        {
            ref var pos = ref entity.GetComponent<TilePosition>();
            result[(pos.TileX, pos.TileY)] = entity.Id;
        }
        return result;
    }

    private Dictionary<(int, int), int> CollectChopDesignations()
    {
        var query = _world.Store.Query<Designation, TilePosition>();
        var result = new Dictionary<(int, int), int>(query.Count);
        foreach (var entity in query.Entities)
        {
            ref var d = ref entity.GetComponent<Designation>();
            if (d.Kind != DesignationKind.ChopTree) continue;
            ref var pos = ref entity.GetComponent<TilePosition>();
            result[(pos.TileX, pos.TileY)] = entity.Id;
        }
        return result;
    }
}
