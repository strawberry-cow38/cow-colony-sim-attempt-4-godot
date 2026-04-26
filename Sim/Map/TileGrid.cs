using System.Runtime.CompilerServices;

namespace CowColonySim.Sim.Map;

// Dense 3D tile flag grid. Layout: x-major, then y, then z. Index math is
// inlined for the hot pathfinding/lighting paths.
public sealed class TileGrid
{
    public int Width { get; }
    public int Height { get; }
    public int MinZ { get; }
    public int MaxZ { get; }
    public int Depth => MaxZ - MinZ + 1;

    public TileFlags[] Flags { get; }

    public TileGrid(MapSettings settings)
    {
        if (settings.Width <= 0 || settings.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(settings),
                "TileGrid requires positive width and height.");
        }
        if (settings.MaxZ < settings.MinZ)
        {
            throw new ArgumentOutOfRangeException(nameof(settings),
                "MaxZ must be >= MinZ.");
        }
        Width = settings.Width;
        Height = settings.Height;
        MinZ = settings.MinZ;
        MaxZ = settings.MaxZ;
        Flags = new TileFlags[Width * Height * Depth];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Index(int x, int y, int z)
    {
        var zi = z - MinZ;
        return (zi * Height + y) * Width + x;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool InBounds(int x, int y, int z) =>
        (uint)x < (uint)Width
        && (uint)y < (uint)Height
        && z >= MinZ && z <= MaxZ;
}
