using System.Runtime.CompilerServices;

namespace CowColonySim.Sim.Map;

public sealed class TileGrid
{
    public MapSettings Settings { get; }
    public TileFlags[] Flags { get; }
    public byte[] ArtificialLight { get; }

    public int Width => Settings.Width;
    public int Height => Settings.Height;
    public int Depth => Settings.Depth;
    public int MinZ => Settings.MinZ;
    public int MaxZ => Settings.MaxZ;

    public TileGrid(MapSettings settings)
    {
        Settings = settings;
        var n = settings.TileCount;
        Flags = new TileFlags[n];
        ArtificialLight = new byte[n];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Index(int x, int y, int z)
    {
        return ((z - MinZ) * Height + y) * Width + x;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool InBounds(int x, int y, int z) =>
        (uint)x < (uint)Width && (uint)y < (uint)Height && z >= MinZ && z < MaxZ;

    public TileFlags GetFlags(int x, int y, int z) => Flags[Index(x, y, z)];

    public void SetFlag(int x, int y, int z, TileFlags flag, bool value)
    {
        ref var slot = ref Flags[Index(x, y, z)];
        slot = value ? slot | flag : slot & ~flag;
    }
}
