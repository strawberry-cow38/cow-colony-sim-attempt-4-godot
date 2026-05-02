using CowColonySim.Sim.Designations;
using CowColonySim.Sim.Pathfinding;
using CowColonySim.Sim.World;
using CowColonySim.Sim.World.Components;
using Friflo.Engine.ECS;

namespace CowColonySim.Sim.Systems;

// WorkKind.SwitchLamp executor. Walks the assigned colonist toward the
// lamp tile (adjacent counts — lamps are footprint=1 so the lamp tile
// itself works), flips LampSwitch.On on arrival, bumps PowerVersion so
// PowerSystem refreshes the demand totals next tick, and clears the job.
//
// Mirrors ForcePickupSystem in shape: queries Colonist + Job + WorkJob
// + TilePosition + PathFollower, skips drafted/already-busy colonists,
// uses PathPlanner.Request via EnsurePath when the colonist isn't on
// the target tile yet.
public sealed class SwitchSystem : ITickSystem
{
    private readonly SimWorld _world;
    private readonly PathPlanner _planner;
    private readonly HeightGrid _grid;

    public SwitchSystem(SimWorld world, PathPlanner planner, HeightGrid grid)
    {
        _world = world;
        _planner = planner;
        _grid = grid;
    }

    public void Tick(TickContext ctx)
    {
        var query = _world.Store.Query<Colonist, Job, WorkJob, TilePosition, PathFollower>();
        foreach (var entity in query.Entities)
        {
            if (entity.HasComponent<Drafted>() && entity.GetComponent<Drafted>().Active) continue;
            ref var job = ref entity.GetComponent<Job>();
            if (job.Active) continue;
            ref var work = ref entity.GetComponent<WorkJob>();
            if (!work.Active || work.Kind != WorkKind.SwitchLamp) continue;

            var lamp = _world.Store.GetEntityById(work.TargetEntityId);
            if (lamp == default || !lamp.HasComponent<LampSwitch>() || !lamp.HasComponent<TilePosition>())
            {
                ClearWork(entity, ref work);
                continue;
            }
            ref var lampPos = ref lamp.GetComponent<TilePosition>();
            ref var pos = ref entity.GetComponent<TilePosition>();
            ref var pf = ref entity.GetComponent<PathFollower>();

            if (pos.TileX != lampPos.TileX || pos.TileY != lampPos.TileY)
            {
                if (pf.LastPathFailed) { ClearWork(entity, ref work); continue; }
                EnsurePath(entity, ref pf, pos.TileX, pos.TileY, pos.TileZ, lampPos.TileX, lampPos.TileY);
                continue;
            }

            ref var sw = ref lamp.GetComponent<LampSwitch>();
            sw.On = !sw.On;
            _world.BumpPowerVersion();
            ClearWork(entity, ref work);
        }
    }

    private void EnsurePath(Entity entity, ref PathFollower pf, int fromX, int fromY, int fromZ, int toX, int toY)
    {
        if (pf.PendingRequest) return;
        if (pf.Tiles is not null && pf.Index < pf.Tiles.Length)
        {
            var last = pf.Tiles[pf.Tiles.Length - 1];
            if (last.X == toX && last.Y == toY) return;
        }
        var start = _grid.NodeAtOrFloor(fromX, fromY, fromZ);
        var goal = _grid.At(toX, toY);
        if (start == goal) { pf.Tiles = null; pf.Index = 0; return; }
        pf.Tiles = null;
        pf.Index = 0;
        pf.PendingRequest = true;
        pf.PlayerForced = true;
        pf.LastPathFailed = false;
        _planner.Request(entity.Id, start, goal);
    }

    private static void ClearWork(Entity entity, ref WorkJob work)
    {
        work.Active = false;
        work.Kind = WorkKind.None;
        work.TargetEntityId = 0;
        work.Progress = 0f;
        work.Forced = false;
        if (entity.HasComponent<PathFollower>())
        {
            ref var pf = ref entity.GetComponent<PathFollower>();
            pf.Tiles = null;
            pf.Index = 0;
        }
    }
}
