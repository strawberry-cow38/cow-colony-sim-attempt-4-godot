using CowColonySim.Sim.Logging;
using CowColonySim.Sim.Pathfinding;
using CowColonySim.Sim.World;
using CowColonySim.Sim.World.Components;
using Friflo.Engine.ECS;

namespace CowColonySim.Sim.Systems;

// Smushes any colonist that ends up on a blocked tile out to the
// nearest walkable neighbour. Trees grown inside zones, blueprints
// finishing under a colonist, or terrain edits dropped on top of a
// pawn all leave colonists trapped — they then sit forever because
// every path request fails the start-tile check. BFS up to a small
// radius and snap them.
public sealed class UnstickSystem : ITickSystem
{
    private const int SearchRadius = 8;

    private readonly SimWorld _world;
    private readonly HeightGrid _grid;

    public UnstickSystem(SimWorld world, HeightGrid grid)
    {
        _world = world;
        _grid = grid;
    }

    public void Tick(TickContext ctx)
    {
        var query = _world.Store.Query<Colonist, TilePosition, PathFollower>();
        foreach (var entity in query.Entities)
        {
            ref var pos = ref entity.GetComponent<TilePosition>();
            if (!InBounds(pos.TileX, pos.TileY)) continue;
            if (!_grid.IsBlocked(pos.TileX, pos.TileY)) continue;
            if (!TryFindNearestWalkable(pos.TileX, pos.TileY, out var nx, out var ny)) continue;
            SimLog.Logger.Information(
                "UNSTICK colonist={Cid} from=({FX},{FY}) to=({TX},{TY})",
                entity.Id, pos.TileX, pos.TileY, nx, ny);
            pos.TileX = nx;
            pos.TileY = ny;
            pos.SubX = 0.5f;
            pos.SubY = 0.5f;
            ref var pf = ref entity.GetComponent<PathFollower>();
            pf.Tiles = null;
            pf.Index = 0;
            pf.PendingRequest = false;
            pf.LastPathFailed = false;
        }
    }

    private bool InBounds(int x, int y) =>
        (uint)x < (uint)_grid.Width && (uint)y < (uint)_grid.Height;

    // Spiral BFS — closest walkable tile wins. Bounded radius so a
    // colonist marooned in the middle of a giant blocked region doesn't
    // teleport across the map.
    private bool TryFindNearestWalkable(int sx, int sy, out int nx, out int ny)
    {
        nx = 0;
        ny = 0;
        for (var r = 1; r <= SearchRadius; r++)
        {
            for (var dy = -r; dy <= r; dy++)
            {
                for (var dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                    var x = sx + dx;
                    var y = sy + dy;
                    if (!InBounds(x, y)) continue;
                    if (_grid.IsBlocked(x, y)) continue;
                    nx = x;
                    ny = y;
                    return true;
                }
            }
        }
        return false;
    }
}
