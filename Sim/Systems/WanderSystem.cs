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
                // Cancel the rest of any draft-move chain — better to stop
                // than to silently skip a missing leg and pretend nothing
                // went wrong.
                pf.WaypointQueue?.Clear();
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

            var drafted = entity.HasComponent<Drafted>() && entity.GetComponent<Drafted>().Active;

            if (pf.Tiles is null || pf.Index >= pf.Tiles.Length)
            {
                pf.Tiles = null;
                pf.PlayerForced = false;
                // Drafted colonists with queued waypoints (shift-RMB chain):
                // pop the head and ask the planner for the next leg before
                // anything else considers them idle.
                if (drafted && !pf.PendingRequest && pf.WaypointQueue is { Count: > 0 } queue)
                {
                    var next = queue[0];
                    queue.RemoveAt(0);
                    if (_grid.InBounds(next))
                    {
                        var qStart = _grid.At(
                            Math.Clamp(pos.TileX, 0, _grid.Width - 1),
                            Math.Clamp(pos.TileY, 0, _grid.Height - 1));
                        pf.PendingRequest = true;
                        pf.PlayerForced = true;
                        _planner.Request(entity.Id, qStart, _grid.At(next.X, next.Y));
                    }
                    continue;
                }
                if (!drafted && !pf.PendingRequest && !job.Active && !work.Active)
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

    private const int WanderRadius = 20;

    private void RequestRandomPath(Entity entity, TilePosition pos)
    {
        var start = _grid.At(
            Math.Clamp(pos.TileX, 0, _grid.Width - 1),
            Math.Clamp(pos.TileY, 0, _grid.Height - 1));

        // Anchor on a built structure if any exist; otherwise the map centre.
        // Cows then meander inside a 20-tile box around the anchor instead of
        // wandering off into the wilderness with no buildings near them.
        PickAnchor(out var anchorX, out var anchorY);
        var gx = anchorX + (int)(NextU32() % (uint)(WanderRadius * 2 + 1)) - WanderRadius;
        var gy = anchorY + (int)(NextU32() % (uint)(WanderRadius * 2 + 1)) - WanderRadius;
        gx = Math.Clamp(gx, 0, _grid.Width - 1);
        gy = Math.Clamp(gy, 0, _grid.Height - 1);
        if (gx == start.X && gy == start.Y)
        {
            gx = (gx + 1) % _grid.Width;
        }
        _planner.Request(entity.Id, start, _grid.At(gx, gy));
    }

    private void PickAnchor(out int tileX, out int tileY)
    {
        // Includes the map centre as one candidate alongside every built
        // structure, so wander targets stay near the centre even when the
        // colony has only a couple of structures placed.
        var query = _world.Store.Query<Structure, TilePosition>();
        var count = query.Count;
        var pickIndex = (int)(NextU32() % (uint)(count + 1));
        if (pickIndex < count)
        {
            var i = 0;
            foreach (var e in query.Entities)
            {
                if (i == pickIndex)
                {
                    ref var p = ref e.GetComponent<TilePosition>();
                    tileX = p.TileX;
                    tileY = p.TileY;
                    return;
                }
                i++;
            }
        }
        tileX = _grid.Width / 2;
        tileY = _grid.Height / 2;
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
