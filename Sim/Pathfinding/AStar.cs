namespace CowColonySim.Sim.Pathfinding;

// Plain 8-connected A* over a HeightGrid. Each call allocates its own
// scratch buffers, so the function is thread-safe — multiple workers can
// run TryFind in parallel against a shared HeightGrid as long as the
// underlying Heightfield isn't being mutated.
public static class AStar
{
    private static readonly (int dx, int dy)[] Neighbours =
    {
        (1, 0), (-1, 0), (0, 1), (0, -1),
        (1, 1), (1, -1), (-1, 1), (-1, -1),
    };

    public static bool TryFind(
        HeightGrid grid, TileCoord start, TileCoord goal, List<TileCoord> outPath)
    {
        outPath.Clear();
        if (!grid.InBounds(start) || !grid.InBounds(goal)) return false;
        if (start == goal)
        {
            outPath.Add(start);
            return true;
        }

        var size = grid.Width * grid.Height;
        var gScore = new float[size];
        var came = new int[size];
        var visited = new bool[size];
        Array.Fill(gScore, float.PositiveInfinity);
        Array.Fill(came, -1);

        var open = new PriorityQueue<int, float>();
        var startIdx = Index(grid, start);
        var goalIdx = Index(grid, goal);
        gScore[startIdx] = 0f;
        open.Enqueue(startIdx, HeightGrid.OctileHeuristic(start, goal));

        while (open.TryDequeue(out var currentIdx, out _))
        {
            if (visited[currentIdx]) continue;
            visited[currentIdx] = true;
            if (currentIdx == goalIdx)
            {
                Reconstruct(came, currentIdx, startIdx, grid, outPath);
                return true;
            }

            var current = FromIndex(grid, currentIdx);
            for (var i = 0; i < Neighbours.Length; i++)
            {
                var (dx, dy) = Neighbours[i];
                var next = new TileCoord(current.X + dx, current.Y + dy);
                if (!grid.CanStep(current, next)) continue;
                var nextIdx = Index(grid, next);
                if (visited[nextIdx]) continue;
                var tentative = gScore[currentIdx] + grid.StepCost(current, next);
                if (tentative < gScore[nextIdx])
                {
                    gScore[nextIdx] = tentative;
                    came[nextIdx] = currentIdx;
                    var f = tentative + HeightGrid.OctileHeuristic(next, goal);
                    open.Enqueue(nextIdx, f);
                }
            }
        }
        return false;
    }

    private static int Index(HeightGrid grid, TileCoord t) => t.Y * grid.Width + t.X;

    private static TileCoord FromIndex(HeightGrid grid, int idx) =>
        new(idx % grid.Width, idx / grid.Width);

    private static void Reconstruct(
        int[] came, int goalIdx, int startIdx, HeightGrid grid, List<TileCoord> outPath)
    {
        var idx = goalIdx;
        while (idx != startIdx)
        {
            outPath.Add(FromIndex(grid, idx));
            idx = came[idx];
        }
        outPath.Add(FromIndex(grid, startIdx));
        outPath.Reverse();
    }
}
