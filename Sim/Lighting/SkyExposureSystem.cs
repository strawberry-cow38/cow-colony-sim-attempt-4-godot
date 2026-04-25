using CowColonySim.Sim.Map;

namespace CowColonySim.Sim.Lighting;

public sealed class SkyExposureSystem
{
    private readonly TileGrid _grid;
    private readonly HashSet<long> _dirty = new();

    public SkyExposureSystem(TileGrid grid)
    {
        _grid = grid;
    }

    public int DirtyColumnCount => _dirty.Count;

    public void MarkColumnDirty(int x, int y)
    {
        if ((uint)x >= (uint)_grid.Width || (uint)y >= (uint)_grid.Height)
        {
            return;
        }
        _dirty.Add(Pack(x, y));
    }

    public void RebuildAll()
    {
        for (var y = 0; y < _grid.Height; y++)
        {
            for (var x = 0; x < _grid.Width; x++)
            {
                RebuildColumn(x, y);
            }
        }
        _dirty.Clear();
    }

    public void RebuildDirty()
    {
        if (_dirty.Count == 0)
        {
            return;
        }
        foreach (var key in _dirty)
        {
            Unpack(key, out var x, out var y);
            RebuildColumn(x, y);
        }
        _dirty.Clear();
    }

    public void RebuildColumn(int x, int y)
    {
        var blocked = false;
        for (var z = _grid.MaxZ - 1; z >= _grid.MinZ; z--)
        {
            var idx = _grid.Index(x, y, z);
            var flag = _grid.Flags[idx];
            flag = blocked
                ? flag & ~TileFlags.ExposedToSky
                : flag | TileFlags.ExposedToSky;
            _grid.Flags[idx] = flag;
            if (flag.BlocksVerticalLight())
            {
                blocked = true;
            }
        }
    }

    private static long Pack(int x, int y) => ((long)(uint)y << 32) | (uint)x;

    private static void Unpack(long key, out int x, out int y)
    {
        x = (int)(key & 0xFFFFFFFFL);
        y = (int)(key >> 32);
    }
}
