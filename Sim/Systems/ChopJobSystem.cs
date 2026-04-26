using CowColonySim.Sim.Designations;
using CowColonySim.Sim.Items;
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
    private const int WoodPerTree = 5;

    private readonly SimWorld _world;
    private readonly PathPlanner _planner;
    private readonly HeightGrid _grid;

    public ChopJobSystem(SimWorld world, PathPlanner planner, HeightGrid grid)
    {
        _world = world;
        _planner = planner;
        _grid = grid;
    }

    private readonly List<FelledTree> _felled = new();

    private readonly struct FelledTree
    {
        public readonly int TreeId;
        public readonly int DesignationId;
        public readonly int TileX;
        public readonly int TileY;
        public FelledTree(int treeId, int designationId, int tileX, int tileY)
        {
            TreeId = treeId;
            DesignationId = designationId;
            TileX = tileX;
            TileY = tileY;
        }
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

        _felled.Clear();
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

        // Apply structural changes outside the colonist iteration. Spawning
        // an Item entity or deleting a Tree mid-foreach can invalidate
        // Friflo's archetype storage and crash the sim thread.
        for (var i = 0; i < _felled.Count; i++)
        {
            var f = _felled[i];
            _grid.MarkBlocked(f.TileX, f.TileY, false);
            _world.AddOrMergeItem(f.TileX, f.TileY, ItemKind.Wood, WoodPerTree);
            var tree = _world.Store.GetEntityById(f.TreeId);
            if (tree != default) tree.DeleteEntity();
            var designation = _world.Store.GetEntityById(f.DesignationId);
            if (designation != default) designation.DeleteEntity();
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

        if (!IsAdjacent(pos.TileX, pos.TileY, work.TargetTileX, work.TargetTileY))
        {
            if (pf.Tiles is null && !pf.PendingRequest)
            {
                if (TryFindStandTile(work.TargetTileX, work.TargetTileY, pos.TileX, pos.TileY, out var stand))
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
            _felled.Add(new FelledTree(treeId, designationId, work.TargetTileX, work.TargetTileY));
            ClearWork(ref work, ref pf);
        }
    }

    private static bool IsAdjacent(int ax, int ay, int bx, int by) =>
        Math.Abs(ax - bx) <= 1 && Math.Abs(ay - by) <= 1;

    private bool TryFindStandTile(int treeX, int treeY, int fromX, int fromY, out TileCoord stand)
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
                var nx = treeX + dx;
                var ny = treeY + dy;
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

        if (IsAdjacent(pos.TileX, pos.TileY, bestKey.Item1, bestKey.Item2)) return;

        if (!TryFindStandTile(bestKey.Item1, bestKey.Item2, pos.TileX, pos.TileY, out var stand))
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
