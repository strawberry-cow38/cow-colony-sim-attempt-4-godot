using CowColonySim.Sim.Pathfinding;
using CowColonySim.Sim.World;
using CowColonySim.Sim.World.Components;
using Friflo.Engine.ECS;

namespace CowColonySim.Sim.Systems;

// Drains finished A* results onto matching colonist entities, then walks
// each colonist toward its current path waypoint. When a colonist has no
// path (and no request in flight), pick a random walkable tile and ask
// the PathPlanner. Pure Sim/, no Godot deps.
public sealed class WanderSystem : ITickSystem
{
    private const float SpeedMps = 0.9f;
    private const float WaypointReachedMeters = 0.05f;

    private readonly SimWorld _world;
    private readonly PathPlanner _planner;
    private readonly HeightGrid _grid;
    private uint _rng = 0xA5A5A5A5;

    public WanderSystem(SimWorld world, PathPlanner planner, HeightGrid grid)
    {
        _world = world;
        _planner = planner;
        _grid = grid;
    }

    public void Tick(TickContext ctx)
    {
        DrainResults();
        StepColonists((float)ctx.FixedDeltaSeconds);
    }

    private void DrainResults()
    {
        while (_planner.TryDequeue(out var result))
        {
            var entity = _world.Store.GetEntityById(result.RequesterId);
            if (entity == default || !entity.HasComponent<PathFollower>()) continue;
            ref var pf = ref entity.GetComponent<PathFollower>();
            pf.PendingRequest = false;
            if (result.Found && result.Tiles.Length > 1)
            {
                pf.Tiles = result.Tiles;
                pf.Index = 1;
                pf.LastPathFailed = false;
            }
            else if (!result.Found)
            {
                pf.LastPathFailed = true;
            }
        }
    }

    private void StepColonists(float dt)
    {
        var query = _world.Store.Query<Colonist, TilePosition, PathFollower, Job, WorkJob>();
        foreach (var entity in query.Entities)
        {
            ref var pos = ref entity.GetComponent<TilePosition>();
            ref var pf = ref entity.GetComponent<PathFollower>();
            ref var job = ref entity.GetComponent<Job>();
            ref var work = ref entity.GetComponent<WorkJob>();

            if (pf.Tiles is null || pf.Index >= pf.Tiles.Length)
            {
                pf.Tiles = null;
                pf.PlayerForced = false;
                if (!pf.PendingRequest && !job.Active && !work.Active)
                {
                    RequestRandomPath(entity, pos);
                    pf.PendingRequest = true;
                    pf.PlayerForced = false;
                }
                continue;
            }

            var target = pf.Tiles[pf.Index];
            var targetMx = (target.X + 0.5f) * SimConstants.MetersPerTile;
            var targetMy = (target.Y + 0.5f) * SimConstants.MetersPerTile;
            var dx = targetMx - pos.MetersX;
            var dy = targetMy - pos.MetersY;
            var dist = MathF.Sqrt(dx * dx + dy * dy);

            if (dist <= WaypointReachedMeters)
            {
                pos.TileZ = target.Z;
                pf.Index++;
                continue;
            }

            var step = SpeedMps * dt;
            if (step >= dist)
            {
                WriteMetersXY(ref pos, targetMx, targetMy);
                pos.TileZ = target.Z;
                pf.Index++;
            }
            else
            {
                var nx = pos.MetersX + dx / dist * step;
                var ny = pos.MetersY + dy / dist * step;
                WriteMetersXY(ref pos, nx, ny);
            }
        }
    }

    private void RequestRandomPath(Entity entity, TilePosition pos)
    {
        var start = _grid.At(
            Math.Clamp(pos.TileX, 0, _grid.Width - 1),
            Math.Clamp(pos.TileY, 0, _grid.Height - 1));
        var goal = _grid.At(
            (int)(NextU32() % (uint)_grid.Width),
            (int)(NextU32() % (uint)_grid.Height));
        if (goal.X == start.X && goal.Y == start.Y)
        {
            goal = _grid.At((goal.X + 1) % _grid.Width, goal.Y);
        }
        _planner.Request(entity.Id, start, goal);
    }

    private static void WriteMetersXY(ref TilePosition p, float metersX, float metersY)
    {
        var tilesX = metersX / SimConstants.MetersPerTile;
        var tilesY = metersY / SimConstants.MetersPerTile;
        var tx = (int)Math.Floor(tilesX);
        var ty = (int)Math.Floor(tilesY);
        p.TileX = tx;
        p.TileY = ty;
        p.SubX = (float)(tilesX - tx);
        p.SubY = (float)(tilesY - ty);
    }

    private uint NextU32()
    {
        var x = _rng == 0 ? 0xDEADBEEFu : _rng;
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        _rng = x;
        return x;
    }
}
