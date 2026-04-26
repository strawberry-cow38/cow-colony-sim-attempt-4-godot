using CowColonySim.Sim.Commands;
using CowColonySim.Sim.Pathfinding;
using CowColonySim.Sim.World;
using CowColonySim.Sim.World.Components;

namespace CowColonySim.Sim.Systems;

// Dispatches queued commands at the top of each tick. Currently only
// MoveCommand: validates the entity, fires an A* request, clears the
// existing path so the colonist commits to the new destination as soon
// as the result lands.
public sealed class CommandSystem : ITickSystem
{
    private readonly CommandBus _bus;
    private readonly SimWorld _world;
    private readonly PathPlanner _planner;
    private readonly HeightGrid _grid;

    public CommandSystem(CommandBus bus, SimWorld world, PathPlanner planner, HeightGrid grid)
    {
        _bus = bus;
        _world = world;
        _planner = planner;
        _grid = grid;
    }

    public void Tick(TickContext ctx)
    {
        while (_bus.TryDequeue(out var command))
        {
            switch (command)
            {
                case MoveCommand move:
                    Apply(move);
                    break;
                case InvalidatePathsInRegion region:
                    Apply(region);
                    break;
            }
        }
    }

    private void Apply(InvalidatePathsInRegion region)
    {
        var query = _world.Store.Query<PathFollower>();
        foreach (var entity in query.Entities)
        {
            ref var pf = ref entity.GetComponent<PathFollower>();
            if (pf.Tiles is null) continue;
            for (var i = pf.Index; i < pf.Tiles.Length; i++)
            {
                var t = pf.Tiles[i];
                if (t.X >= region.MinTileX && t.X <= region.MaxTileX
                    && t.Y >= region.MinTileY && t.Y <= region.MaxTileY)
                {
                    pf.Tiles = null;
                    pf.Index = 0;
                    break;
                }
            }
        }
    }

    private void Apply(MoveCommand move)
    {
        var entity = _world.Store.GetEntityById(move.EntityId);
        if (entity == default) return;
        if (!entity.HasComponent<PathFollower>()) return;
        if (!entity.HasComponent<TilePosition>()) return;
        if (!_grid.InBounds(move.Target)) return;

        ref var pos = ref entity.GetComponent<TilePosition>();
        ref var pf = ref entity.GetComponent<PathFollower>();
        var start = _grid.At(
            Math.Clamp(pos.TileX, 0, _grid.Width - 1),
            Math.Clamp(pos.TileY, 0, _grid.Height - 1));
        var goal = _grid.At(move.Target.X, move.Target.Y);
        pf.Tiles = null;
        pf.Index = 0;
        pf.PendingRequest = true;
        pf.PlayerForced = true;
        _planner.Request(entity.Id, start, goal);
    }
}
