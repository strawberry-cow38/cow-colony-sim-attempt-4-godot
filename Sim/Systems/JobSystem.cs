using CowColonySim.Sim.Designations;
using CowColonySim.Sim.Pathfinding;
using CowColonySim.Sim.World;
using CowColonySim.Sim.World.Components;
using Friflo.Engine.ECS;

namespace CowColonySim.Sim.Systems;

// Assigns colonists to need-satisfying spots and processes consumption.
//
// Per tick, for each colonist:
//   1) If the colonist has no active job and a need is below the hunt
//      threshold, find the lowest-need kind, locate the nearest matching
//      NeedSpot, set Job, and dispatch an A* request.
//   2) If the colonist's tile equals the job target, refill that need at
//      the spot's SatisfyPerSec rate. Once full, clear the job.
//   3) Otherwise let WanderSystem keep the colonist moving (handled in
//      that system; JobSystem just doesn't override anything here).
public sealed class JobSystem : ITickSystem
{
    private const float HuntThreshold = 40f;
    private const float TileMatchMeters = 0.4f;

    private readonly SimWorld _world;
    private readonly PathPlanner _planner;
    private readonly HeightGrid _grid;

    public JobSystem(SimWorld world, PathPlanner planner, HeightGrid grid)
    {
        _world = world;
        _planner = planner;
        _grid = grid;
    }

    public void Tick(TickContext ctx)
    {
        var dt = (float)ctx.FixedDeltaSeconds;
        var spots = CollectSpots();

        var query = _world.Store.Query<Colonist, Needs, Job, TilePosition, WorkJob>();
        foreach (var entity in query.Entities)
        {
            if (entity.HasComponent<Drafted>() && entity.GetComponent<Drafted>().Active) continue;
            ref var needs = ref entity.GetComponent<Needs>();
            ref var job = ref entity.GetComponent<Job>();
            ref var pos = ref entity.GetComponent<TilePosition>();
            ref var pf = ref entity.GetComponent<PathFollower>();
            ref var work = ref entity.GetComponent<WorkJob>();

            // Player-forced work overrides everything — they'll work until
            // they die before we let needs hijack the colonist. Hauls are
            // also non-preemptable: dropping a stack mid-trip leaves the
            // item stranded between source and dest, so we ride out the
            // haul before letting needs grab the colonist.
            if (work.Active && (work.Forced || work.Kind == WorkKind.HaulItem))
            {
                if (job.Active)
                {
                    job.Active = false;
                    pf.Tiles = null;
                    pf.Index = 0;
                }
                continue;
            }

            if (job.Active)
            {
                ProgressJob(entity, ref job, ref needs, ref pos, ref pf, spots, dt);
            }
            else
            {
                TryAssignJob(entity, ref needs, ref job, ref pf, ref pos, spots);
            }
        }
    }

    private void ProgressJob(
        Entity entity, ref Job job, ref Needs needs, ref TilePosition pos,
        ref PathFollower pf, List<SpotRecord> spots, float dt)
    {
        var arrived = pos.TileX == job.TargetTileX && pos.TileY == job.TargetTileY;
        if (!arrived) return;

        var rate = 0f;
        for (var i = 0; i < spots.Count; i++)
        {
            var s = spots[i];
            if (s.Kind != job.NeedKind) continue;
            if (s.TileX != job.TargetTileX || s.TileY != job.TargetTileY) continue;
            rate = s.SatisfyPerSec;
            break;
        }
        if (rate <= 0f)
        {
            job.Active = false;
            return;
        }

        var current = needs.Get(job.NeedKind);
        var refilled = MathF.Min(100f, current + rate * dt);
        needs.Set(job.NeedKind, refilled);
        if (refilled >= 100f)
        {
            job.Active = false;
            pf.Tiles = null;
            pf.Index = 0;
        }
    }

    private void TryAssignJob(
        Entity entity, ref Needs needs, ref Job job, ref PathFollower pf,
        ref TilePosition pos, List<SpotRecord> spots)
    {
        var lowestKind = NeedKind.Hunger;
        var lowestValue = needs.Hunger;
        if (needs.Thirst < lowestValue) { lowestKind = NeedKind.Thirst; lowestValue = needs.Thirst; }
        if (needs.Energy < lowestValue) { lowestKind = NeedKind.Energy; lowestValue = needs.Energy; }
        if (lowestValue >= HuntThreshold) return;

        var bestIdx = -1;
        var bestDistSq = float.PositiveInfinity;
        for (var i = 0; i < spots.Count; i++)
        {
            if (spots[i].Kind != lowestKind) continue;
            var dx = spots[i].TileX - pos.TileX;
            var dy = spots[i].TileY - pos.TileY;
            var d = dx * dx + dy * dy;
            if (d < bestDistSq)
            {
                bestDistSq = d;
                bestIdx = i;
            }
        }
        if (bestIdx < 0) return;

        var spot = spots[bestIdx];
        job.Active = true;
        job.NeedKind = lowestKind;
        job.TargetTileX = spot.TileX;
        job.TargetTileY = spot.TileY;

        var start = _grid.At(
            Math.Clamp(pos.TileX, 0, _grid.Width - 1),
            Math.Clamp(pos.TileY, 0, _grid.Height - 1));
        var goal = _grid.At(spot.TileX, spot.TileY);
        if (start == goal) return;
        pf.Tiles = null;
        pf.Index = 0;
        pf.PendingRequest = true;
        pf.PlayerForced = false;
        _planner.Request(entity.Id, start, goal);
    }

    private List<SpotRecord> CollectSpots()
    {
        var query = _world.Store.Query<NeedSpot, TilePosition>();
        var result = new List<SpotRecord>(query.Count);
        foreach (var entity in query.Entities)
        {
            ref var spot = ref entity.GetComponent<NeedSpot>();
            ref var pos = ref entity.GetComponent<TilePosition>();
            result.Add(new SpotRecord(spot.Kind, spot.SatisfyPerSec, pos.TileX, pos.TileY));
        }
        return result;
    }

    private readonly record struct SpotRecord(NeedKind Kind, float SatisfyPerSec, int TileX, int TileY);
}
