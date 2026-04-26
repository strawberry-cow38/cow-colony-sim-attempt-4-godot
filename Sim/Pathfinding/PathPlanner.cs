namespace CowColonySim.Sim.Pathfinding;

// Off-thread A* dispatcher. Tick() drains completed jobs back onto the
// caller thread; Request() pushes a new job into the .NET thread pool.
// Used by gameplay systems that want a path without blocking the SimThread.
public sealed class PathPlanner
{
    private readonly HeightGrid _grid;
    private readonly System.Collections.Concurrent.ConcurrentQueue<PathResult> _completed = new();

    public PathPlanner(HeightGrid grid)
    {
        _grid = grid;
    }

    public void Request(int requesterId, TileCoord start, TileCoord goal)
    {
        Task.Run(() =>
        {
            var path = new List<TileCoord>(32);
            var ok = AStar.TryFind(_grid, start, goal, path);
            _completed.Enqueue(new PathResult(requesterId, ok, ok ? path.ToArray() : Array.Empty<TileCoord>()));
        });
    }

    public bool TryDequeue(out PathResult result) => _completed.TryDequeue(out result!);
}

public readonly record struct PathResult(int RequesterId, bool Found, TileCoord[] Tiles);
