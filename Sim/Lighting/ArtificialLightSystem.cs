using CowColonySim.Sim.Map;
using Friflo.Engine.ECS;

namespace CowColonySim.Sim.Lighting;

public sealed class ArtificialLightSystem
{
    private static readonly (int dx, int dy, int dz)[] Neighbors6 = new[]
    {
        (1, 0, 0), (-1, 0, 0),
        (0, 1, 0), (0, -1, 0),
        (0, 0, 1), (0, 0, -1),
    };

    private readonly TileGrid _grid;
    private readonly EntityStore _store;
    private readonly Queue<(int x, int y, int z, int distance)> _frontier = new();
    private readonly HashSet<int> _visited = new();
    private bool _dirty = true;

    public bool IsDirty => _dirty;

    public ArtificialLightSystem(TileGrid grid, EntityStore store)
    {
        _grid = grid;
        _store = store;
    }

    public void MarkDirty() => _dirty = true;

    public void RebuildIfDirty()
    {
        if (!_dirty)
        {
            return;
        }
        Rebuild();
    }

    public void Rebuild()
    {
        Array.Clear(_grid.ArtificialLight);
        var query = _store.Query<TileCoord, LightEmitter>();
        query.ForEachEntity((ref TileCoord pos, ref LightEmitter emit, Entity _) =>
        {
            FloodFill(pos, emit);
        });
        _dirty = false;
    }

    private void FloodFill(TileCoord origin, LightEmitter emit)
    {
        if (!_grid.InBounds(origin.X, origin.Y, origin.Z))
        {
            return;
        }
        if (emit.Radius <= 0 || emit.Intensity == 0)
        {
            return;
        }

        var clampedIntensity = Math.Min(emit.Intensity, LightConstants.ArtificialMax);

        _frontier.Clear();
        _visited.Clear();

        var startIdx = _grid.Index(origin.X, origin.Y, origin.Z);
        _frontier.Enqueue((origin.X, origin.Y, origin.Z, 0));
        _visited.Add(startIdx);

        while (_frontier.Count > 0)
        {
            var (x, y, z, d) = _frontier.Dequeue();
            var idx = _grid.Index(x, y, z);

            var contribution = (byte)((clampedIntensity * (emit.Radius - d)) / emit.Radius);
            if (contribution > _grid.ArtificialLight[idx])
            {
                _grid.ArtificialLight[idx] = contribution;
            }

            if (d >= emit.Radius)
            {
                continue;
            }

            if (d > 0 && _grid.Flags[idx].BlocksAnyLight())
            {
                continue;
            }

            foreach (var (dx, dy, dz) in Neighbors6)
            {
                var nx = x + dx;
                var ny = y + dy;
                var nz = z + dz;
                if (!_grid.InBounds(nx, ny, nz))
                {
                    continue;
                }
                var nidx = _grid.Index(nx, ny, nz);
                if (!_visited.Add(nidx))
                {
                    continue;
                }
                _frontier.Enqueue((nx, ny, nz, d + 1));
            }
        }
    }
}
