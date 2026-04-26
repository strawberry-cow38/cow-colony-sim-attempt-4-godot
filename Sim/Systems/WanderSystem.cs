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
            }
        }
    }

    private void StepColonists(float dt)
    {
        var query = _world.Store.Query<Colonist, TilePosition, PathFollower>();
        foreach (var entity in query.Entities)
        {
            ref var pos = ref entity.GetComponent<TilePosition>();
            ref var pf = ref entity.GetComponent<PathFollower>();

            if (pf.Tiles is null || pf.Index >= pf.Tiles.Length)
            {
                if (!pf.PendingRequest)
                {
                    RequestRandomPath(entity, pos);
                    pf.PendingRequest = true;
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
                pf.Index++;
                continue;
            }

            var step = SpeedMps * dt;
            if (step >= dist)
            {
                WriteMetersXY(ref pos, targetMx, targetMy);
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
        var start = new TileCoord(
            Math.Clamp(pos.TileX, 0, _grid.Width - 1),
            Math.Clamp(pos.TileY, 0, _grid.Height - 1));
        var goal = new TileCoord(
            (int)(NextU32() % (uint)_grid.Width),
            (int)(NextU32() % (uint)_grid.Height));
        if (goal == start)
        {
            goal = new TileCoord((goal.X + 1) % _grid.Width, goal.Y);
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
