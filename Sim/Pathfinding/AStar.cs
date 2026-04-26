namespace CowColonySim.Sim.Pathfinding;

// 8-connected 3D A* over a HeightGrid. Each call allocates its own scratch
// dictionaries, so the function is thread-safe — multiple workers can run
// TryFind in parallel against a shared HeightGrid as long as the underlying
// Heightfield isn't being mutated.
//
// Nodes are full TileCoords (X, Y, Z); neighbours are enumerated via
// HeightGrid.LayerCountAt + LayerAt so a tile that exposes multiple
// walkable surfaces (a ramp or stair) lights up extra edges without any
// change here. Sparse Dictionary scratch — cost scales with explored
// frontier, not Width*Height*LayerRange.
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

        var gScore = new Dictionary<TileCoord, float>();
        var came = new Dictionary<TileCoord, TileCoord>();
        var visited = new HashSet<TileCoord>();
        var open = new PriorityQueue<TileCoord, float>();

        gScore[start] = 0f;
        open.Enqueue(start, HeightGrid.OctileHeuristic(start, goal));

        while (open.TryDequeue(out var current, out _))
        {
            if (!visited.Add(current)) continue;
            if (current == goal)
            {
                Reconstruct(came, current, start, outPath);
                return true;
            }

            var currentG = gScore[current];
            for (var i = 0; i < Neighbours.Length; i++)
            {
                var (dx, dy) = Neighbours[i];
                var nx = current.X + dx;
                var ny = current.Y + dy;
                if ((uint)nx >= (uint)grid.Width || (uint)ny >= (uint)grid.Height) continue;

                var layerCount = grid.LayerCountAt(nx, ny);
                for (var li = 0; li < layerCount; li++)
                {
                    var next = grid.NodeAt(nx, ny, li);
                    if (!grid.CanStep(current, next)) continue;
                    if (visited.Contains(next)) continue;
                    var tentative = currentG + grid.StepCost(current, next);
                    if (!gScore.TryGetValue(next, out var existing) || tentative < existing)
                    {
                        gScore[next] = tentative;
                        came[next] = current;
                        var f = tentative + HeightGrid.OctileHeuristic(next, goal);
                        open.Enqueue(next, f);
                    }
                }
            }
        }
        return false;
    }

    private static void Reconstruct(
        Dictionary<TileCoord, TileCoord> came,
        TileCoord goal,
        TileCoord start,
        List<TileCoord> outPath)
    {
        var node = goal;
        while (node != start)
        {
            outPath.Add(node);
            node = came[node];
        }
        outPath.Add(start);
        outPath.Reverse();
    }
}
