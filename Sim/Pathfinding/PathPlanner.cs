using System.Collections.Concurrent;

namespace CowColonySim.Sim.Pathfinding;

// Off-thread A* dispatcher with a bounded worker pool. Tick() drains
// completed jobs back onto the caller thread; Request() pushes a new job
// into the .NET thread pool but only MaxConcurrency jobs run A* at once —
// the rest queue on a SemaphoreSlim so a flood of requests can't starve
// other parallel sim work or spike GC from many scratch allocs at once.
public sealed class PathPlanner
{
    private readonly HeightGrid _grid;
    private readonly ConcurrentQueue<PathResult> _completed = new();
    private readonly SemaphoreSlim _gate;

    public PathPlanner(HeightGrid grid)
        : this(grid, DefaultMaxConcurrency()) { }

    public PathPlanner(HeightGrid grid, int maxConcurrency)
    {
        _grid = grid;
        _gate = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        MaxConcurrency = maxConcurrency;
    }

    public int MaxConcurrency { get; }

    public void Request(int requesterId, TileCoord start, TileCoord goal)
    {
        Task.Run(async () =>
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                var path = new List<TileCoord>(32);
                var ok = AStar.TryFind(_grid, start, goal, path);
                _completed.Enqueue(new PathResult(
                    requesterId,
                    ok,
                    ok ? path.ToArray() : Array.Empty<TileCoord>()));
            }
            finally
            {
                _gate.Release();
            }
        });
    }

    public bool TryDequeue(out PathResult result) => _completed.TryDequeue(out result!);

    private static int DefaultMaxConcurrency()
    {
        var n = Environment.ProcessorCount - 1;
        return n < 1 ? 1 : n;
    }
}

public readonly record struct PathResult(int RequesterId, bool Found, TileCoord[] Tiles);
