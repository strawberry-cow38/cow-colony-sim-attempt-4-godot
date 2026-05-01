using CowColonySim.Sim.Designations;
using CowColonySim.Sim.Items;
using CowColonySim.Sim.Pathfinding;
using CowColonySim.Sim.World;
using CowColonySim.Sim.World.Components;
using Friflo.Engine.ECS;

namespace CowColonySim.Sim.Systems;

// Picks Mine designations off the world and walks idle colonists to the
// matching Boulder. While adjacent, Health drains at MineRate per second.
// When Health hits zero the Boulder + Designation entities are deleted, the
// tile is unblocked, and StonePerBoulder Stone items spawn on the tile.
//
// Mirrors ChopJobSystem — share-by-copy is the locked pattern in this repo
// for designation-paired work systems. Need-driven Job preempts.
public sealed class MineJobSystem : ITickSystem
{
    private const float MineIntervalSec = 0.6f;
    private const int DamagePerHit = 3;
    private const int StonePerBoulder = 20;

    private readonly SimWorld _world;
    private readonly PathPlanner _planner;
    private readonly HeightGrid _grid;

    public MineJobSystem(SimWorld world, PathPlanner planner, HeightGrid grid)
    {
        _world = world;
        _planner = planner;
        _grid = grid;
    }

    private readonly List<MinedBoulder> _mined = new();

    private readonly struct MinedBoulder
    {
        public readonly int BoulderId;
        public readonly int DesignationId;
        public readonly int TileX;
        public readonly int TileY;
        public MinedBoulder(int boulderId, int designationId, int tileX, int tileY)
        {
            BoulderId = boulderId;
            DesignationId = designationId;
            TileX = tileX;
            TileY = tileY;
        }
    }

    public void Tick(TickContext ctx)
    {
        var dt = (float)ctx.FixedDeltaSeconds;
        var boulders = CollectBoulders();
        var mineDesignations = CollectMineDesignations();

        var claimed = new HashSet<int>();
        var query = _world.Store.Query<Colonist, Job, WorkJob, TilePosition, PathFollower>();
        foreach (var entity in query.Entities)
        {
            ref var work = ref entity.GetComponent<WorkJob>();
            if (work.Active && work.Kind == WorkKind.Mine) claimed.Add(work.TargetEntityId);
        }

        _mined.Clear();
        foreach (var entity in query.Entities)
        {
            if (entity.HasComponent<Drafted>() && entity.GetComponent<Drafted>().Active) continue;
            ref var job = ref entity.GetComponent<Job>();
            ref var work = ref entity.GetComponent<WorkJob>();
            ref var pos = ref entity.GetComponent<TilePosition>();
            ref var pf = ref entity.GetComponent<PathFollower>();

            if (job.Active) continue;

            if (work.Active && work.Kind == WorkKind.Mine)
            {
                ProgressMine(entity, ref work, ref pf, ref pos, boulders, mineDesignations, dt);
            }
            else if (!work.Active)
            {
                if (entity.HasComponent<WorkPriorities>() &&
                    entity.GetComponent<WorkPriorities>().Get(WorkType.Mining) == 0) continue;
                TryAssignMine(entity, ref work, ref pf, ref pos, boulders, mineDesignations, claimed);
            }
        }

        var seen = new HashSet<int>();
        for (var i = 0; i < _mined.Count; i++)
        {
            var m = _mined[i];
            if (!seen.Add(m.BoulderId)) continue;
            _grid.MarkBlocked(m.TileX, m.TileY, false);
            _world.ClearUnreachableWorkTargets();
            _world.AddOrMergeItem(m.TileX, m.TileY, ItemKind.Stone, StonePerBoulder);
            var boulder = _world.Store.GetEntityById(m.BoulderId);
            if (boulder != default) boulder.DeleteEntity();
            var designation = _world.Store.GetEntityById(m.DesignationId);
            if (designation != default) designation.DeleteEntity();
        }
    }

    private void ProgressMine(
        Entity entity, ref WorkJob work, ref PathFollower pf, ref TilePosition pos,
        Dictionary<(int, int), int> boulders, Dictionary<(int, int), int> mines, float dt)
    {
        var key = (work.TargetTileX, work.TargetTileY);
        if (!boulders.TryGetValue(key, out var boulderId) || boulderId != work.TargetEntityId)
        {
            ClearWork(ref work, ref pf);
            return;
        }
        if (!mines.TryGetValue(key, out var designationId))
        {
            ClearWork(ref work, ref pf);
            return;
        }

        if (!IsAdjacent(pos.TileX, pos.TileY, work.TargetTileX, work.TargetTileY))
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
                    _world.UnreachableWorkTargets.Add(work.TargetEntityId);
                    ClearWork(ref work, ref pf);
                }
            }
            return;
        }

        var boulder = _world.Store.GetEntityById(boulderId);
        if (boulder == default || !boulder.HasComponent<Boulder>())
        {
            ClearWork(ref work, ref pf);
            return;
        }
        ref var b = ref boulder.GetComponent<Boulder>();
        work.Progress += dt;
        if (work.Progress >= MineIntervalSec)
        {
            work.Progress -= MineIntervalSec;
            b.Health = Math.Max(0, b.Health - DamagePerHit);
            b.HitCount++;
        }
        if (b.Health <= 0)
        {
            _mined.Add(new MinedBoulder(boulderId, designationId, work.TargetTileX, work.TargetTileY));
            ClearWork(ref work, ref pf);
        }
    }

    private static bool IsAdjacent(int ax, int ay, int bx, int by) =>
        Math.Abs(ax - bx) <= 1 && Math.Abs(ay - by) <= 1;

    private bool TryFindStandTile(int boulderX, int boulderY, int fromX, int fromY, out TileCoord stand)
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
                var nx = boulderX + dx;
                var ny = boulderY + dy;
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

    private void TryAssignMine(
        Entity entity, ref WorkJob work, ref PathFollower pf, ref TilePosition pos,
        Dictionary<(int, int), int> boulders, Dictionary<(int, int), int> mines,
        HashSet<int> claimed)
    {
        var bestId = 0;
        var bestKey = (0, 0);
        var bestDistSq = float.PositiveInfinity;
        foreach (var key in mines.Keys)
        {
            if (!boulders.TryGetValue(key, out var bid)) continue;
            if (claimed.Contains(bid)) continue;
            if (_world.UnreachableWorkTargets.Contains(bid)) continue;
            var dx = key.Item1 - pos.TileX;
            var dy = key.Item2 - pos.TileY;
            var d = dx * dx + dy * dy;
            if (d < bestDistSq)
            {
                bestDistSq = d;
                bestKey = key;
                bestId = bid;
            }
        }
        if (bestId == 0) return;

        work.Active = true;
        work.Kind = WorkKind.Mine;
        work.TargetTileX = bestKey.Item1;
        work.TargetTileY = bestKey.Item2;
        work.TargetEntityId = bestId;
        work.Progress = 0f;
        claimed.Add(bestId);

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
        work.Forced = false;
        pf.Tiles = null;
        pf.Index = 0;
    }

    private Dictionary<(int, int), int> CollectBoulders()
    {
        var query = _world.Store.Query<Boulder, TilePosition>();
        var result = new Dictionary<(int, int), int>(query.Count);
        foreach (var entity in query.Entities)
        {
            ref var pos = ref entity.GetComponent<TilePosition>();
            result[(pos.TileX, pos.TileY)] = entity.Id;
        }
        return result;
    }

    private Dictionary<(int, int), int> CollectMineDesignations()
    {
        var query = _world.Store.Query<Designation, TilePosition>();
        var result = new Dictionary<(int, int), int>(query.Count);
        foreach (var entity in query.Entities)
        {
            ref var d = ref entity.GetComponent<Designation>();
            if (d.Kind != DesignationKind.Mine) continue;
            ref var pos = ref entity.GetComponent<TilePosition>();
            result[(pos.TileX, pos.TileY)] = entity.Id;
        }
        return result;
    }
}
