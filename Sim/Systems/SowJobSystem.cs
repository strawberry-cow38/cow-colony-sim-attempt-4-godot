using CowColonySim.Sim.Designations;
using CowColonySim.Sim.Pathfinding;
using CowColonySim.Sim.Plants;
using CowColonySim.Sim.World;
using CowColonySim.Sim.World.Components;
using CowColonySim.Sim.Zones;
using Friflo.Engine.ECS;

namespace CowColonySim.Sim.Systems;

// Picks Sow designations off farm tiles and walks idle colonists out
// to plant a seedling. The CropDefId is read off the farm zone that
// owns the target tile at completion time, so a player switching the
// farm's crop mid-job leaves no stale plantings behind.
//
// Trees mark the tile blocked when planted so the pathfinder treats
// saplings the same as wild trees. Non-tree crops leave the tile
// walkable, which lets colonists tend neighboring tiles.
public sealed class SowJobSystem : ITickSystem
{
    private const float SowIntervalSec = 0.55f;

    private readonly SimWorld _world;
    private readonly PathPlanner _planner;
    private readonly HeightGrid _grid;

    public SowJobSystem(SimWorld world, PathPlanner planner, HeightGrid grid)
    {
        _world = world;
        _planner = planner;
        _grid = grid;
    }

    private readonly List<SownTile> _sown = new();

    private readonly struct SownTile
    {
        public readonly int DesignationId;
        public readonly int TileX;
        public readonly int TileY;
        public readonly int CropDefId;
        public SownTile(int designationId, int tileX, int tileY, int cropDefId)
        {
            DesignationId = designationId;
            TileX = tileX;
            TileY = tileY;
            CropDefId = cropDefId;
        }
    }

    public void Tick(TickContext ctx)
    {
        var dt = (float)ctx.FixedDeltaSeconds;
        var sowDes = CollectSowDesignations();
        var farms = CollectFarmsByTile();

        var claimed = new HashSet<int>();
        var query = _world.Store.Query<Colonist, Job, WorkJob, TilePosition, PathFollower>();
        foreach (var entity in query.Entities)
        {
            ref var work = ref entity.GetComponent<WorkJob>();
            if (work.Active && work.Kind == WorkKind.Sow) claimed.Add(work.TargetEntityId);
        }

        _sown.Clear();
        foreach (var entity in query.Entities)
        {
            if (entity.HasComponent<Drafted>() && entity.GetComponent<Drafted>().Active) continue;
            ref var job = ref entity.GetComponent<Job>();
            ref var work = ref entity.GetComponent<WorkJob>();
            ref var pos = ref entity.GetComponent<TilePosition>();
            ref var pf = ref entity.GetComponent<PathFollower>();

            if (job.Active) continue;

            if (work.Active && work.Kind == WorkKind.Sow)
            {
                Progress(entity, ref work, ref pf, ref pos, sowDes, farms, dt);
            }
            else if (!work.Active)
            {
                TryAssign(entity, ref work, ref pf, ref pos, sowDes, farms, claimed);
            }
        }

        // Spawn outside iteration. Plant entity creation triggers archetype
        // mutation which would invalidate Friflo's component refs above.
        for (var i = 0; i < _sown.Count; i++)
        {
            var s = _sown[i];
            // Bail if a plant raced in (shouldn't happen but keeps the
            // invariant clean).
            if (HasPlantAt(s.TileX, s.TileY)) continue;

            var def = CropCatalog.Get(s.CropDefId);
            if (def.IsTree)
            {
                _world.SpawnTree(s.TileX, s.TileY, unchecked((uint)(s.TileX * 73856093 ^ s.TileY * 19349663)),
                    health: 30, growth: 0f);
                _grid.MarkBlocked(s.TileX, s.TileY, true);
            }
            else
            {
                var e = _world.CreateEntity();
                e.AddComponent(new TilePosition(s.TileX, s.TileY, 0, 0.5f, 0.5f));
                e.AddComponent(new Plant
                {
                    Growth = 0f,
                    Age = 0,
                    LifespanTicks = def.LifespanTicks,
                    CropDefId = def.Id,
                    IsTree = false,
                });
            }

            var designation = _world.Store.GetEntityById(s.DesignationId);
            if (designation != default) designation.DeleteEntity();
        }
    }

    private bool HasPlantAt(int tileX, int tileY)
    {
        foreach (var entity in _world.Store.Query<Plant, TilePosition>().Entities)
        {
            ref var pos = ref entity.GetComponent<TilePosition>();
            if (pos.TileX == tileX && pos.TileY == tileY) return true;
        }
        return false;
    }

    private void Progress(
        Entity entity, ref WorkJob work, ref PathFollower pf, ref TilePosition pos,
        Dictionary<(int, int), int> sowDes,
        Dictionary<(int, int), int> farmsByTile,
        float dt)
    {
        var key = (work.TargetTileX, work.TargetTileY);
        if (!sowDes.TryGetValue(key, out var designationId) || designationId != work.TargetEntityId)
        {
            ClearWork(ref work, ref pf);
            return;
        }
        if (!farmsByTile.TryGetValue(key, out var farmId))
        {
            // Farm got erased while colonist was en route. Drop the work.
            ClearWork(ref work, ref pf);
            return;
        }

        if (!IsAt(pos.TileX, pos.TileY, work.TargetTileX, work.TargetTileY))
        {
            if (pf.Tiles is null && !pf.PendingRequest)
            {
                if (TryFindStandTile(work.TargetTileX, work.TargetTileY, out var stand))
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

        work.Progress += dt;
        if (work.Progress < SowIntervalSec) return;

        var farm = _world.Store.GetEntityById(farmId);
        if (farm == default || !farm.HasComponent<FarmSettings>())
        {
            ClearWork(ref work, ref pf);
            return;
        }
        ref var f = ref farm.GetComponent<FarmSettings>();
        _sown.Add(new SownTile(designationId, work.TargetTileX, work.TargetTileY, f.CropDefId));
        ClearWork(ref work, ref pf);
    }

    private void TryAssign(
        Entity entity, ref WorkJob work, ref PathFollower pf, ref TilePosition pos,
        Dictionary<(int, int), int> sowDes,
        Dictionary<(int, int), int> farmsByTile,
        HashSet<int> claimed)
    {
        var bestDistSq = float.PositiveInfinity;
        var bestKey = (0, 0);
        var bestDesignationId = 0;

        foreach (var key in sowDes.Keys)
        {
            if (!farmsByTile.ContainsKey(key)) continue;
            if (_grid.IsBlocked(key.Item1, key.Item2)) continue;
            var designationId = sowDes[key];
            if (claimed.Contains(designationId)) continue;
            var dx = key.Item1 - pos.TileX;
            var dy = key.Item2 - pos.TileY;
            var d = dx * dx + dy * dy;
            if (d >= bestDistSq) continue;
            bestDistSq = d;
            bestKey = key;
            bestDesignationId = designationId;
        }

        if (bestDesignationId == 0) return;

        work.Active = true;
        work.Kind = WorkKind.Sow;
        work.TargetTileX = bestKey.Item1;
        work.TargetTileY = bestKey.Item2;
        work.TargetEntityId = bestDesignationId;
        work.Progress = 0f;
        claimed.Add(bestDesignationId);

        if (IsAt(pos.TileX, pos.TileY, bestKey.Item1, bestKey.Item2)) return;
        if (!TryFindStandTile(bestKey.Item1, bestKey.Item2, out var stand))
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

    private static bool IsAt(int ax, int ay, int bx, int by) => ax == bx && ay == by;

    private bool TryFindStandTile(int targetX, int targetY, out TileCoord stand)
    {
        if ((uint)targetX < (uint)_grid.Width
            && (uint)targetY < (uint)_grid.Height
            && !_grid.IsBlocked(targetX, targetY))
        {
            stand = _grid.At(targetX, targetY);
            return true;
        }
        stand = default;
        return false;
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

    private Dictionary<(int, int), int> CollectSowDesignations()
    {
        var query = _world.Store.Query<Designation, TilePosition>();
        var result = new Dictionary<(int, int), int>(query.Count);
        foreach (var entity in query.Entities)
        {
            ref var d = ref entity.GetComponent<Designation>();
            if (d.Kind != DesignationKind.Sow) continue;
            ref var pos = ref entity.GetComponent<TilePosition>();
            result[(pos.TileX, pos.TileY)] = entity.Id;
        }
        return result;
    }

    // For each farm tile, which farm zone owns it. SowJobSystem uses
    // this to look up CropDefId at execute time.
    private Dictionary<(int, int), int> CollectFarmsByTile()
    {
        var result = new Dictionary<(int, int), int>();
        foreach (var entity in _world.Store.Query<Zone, FarmSettings>().Entities)
        {
            ref var z = ref entity.GetComponent<Zone>();
            if (z.Type != ZoneType.Farm) continue;
            for (var ty = z.Rect.MinY; ty <= z.Rect.MaxY; ty++)
            {
                for (var tx = z.Rect.MinX; tx <= z.Rect.MaxX; tx++)
                {
                    if (!z.ContainsTile(tx, ty)) continue;
                    result[(tx, ty)] = entity.Id;
                }
            }
        }
        return result;
    }
}
