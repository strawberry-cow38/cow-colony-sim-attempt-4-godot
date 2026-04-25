using CowColonySim.Sim.Map;

namespace CowColonySim.Sim.Lighting;

public sealed class TileLightingApi
{
    private readonly TileGrid _grid;

    public byte GlobalSunByte { get; private set; }

    public TileLightingApi(TileGrid grid)
    {
        _grid = grid;
    }

    public void SetGlobalSun(byte sunByte) => GlobalSunByte = sunByte;

    public byte SunAt(int x, int y, int z)
    {
        if (!_grid.InBounds(x, y, z))
        {
            return 0;
        }
        var flags = _grid.Flags[_grid.Index(x, y, z)];
        return (flags & TileFlags.ExposedToSky) != 0 ? GlobalSunByte : (byte)0;
    }

    public byte ArtificialAt(int x, int y, int z)
    {
        if (!_grid.InBounds(x, y, z))
        {
            return 0;
        }
        return _grid.ArtificialLight[_grid.Index(x, y, z)];
    }

    public byte TotalAt(int x, int y, int z)
    {
        var sun = SunAt(x, y, z);
        var arti = ArtificialAt(x, y, z);
        return sun > arti ? sun : arti;
    }
}
