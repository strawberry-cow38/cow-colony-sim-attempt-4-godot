using System.Runtime.CompilerServices;

namespace CowColonySim.Sim.Terrain;

// Vertex grid sitting on tile corners: a (Width+1) × (Height+1) array of
// height samples, one short per corner, measured in 0.75 m quanta from
// world origin. Shared in data — adjacent tiles see the same corner — so
// the AoE2 blocky look comes from how we render (4 unshared corners per
// tile at mesh-build time), not from doubling up the source data.
public sealed class Heightfield
{
    private readonly short[] _heights;

    public int VertWidth { get; }
    public int VertHeight { get; }
    public int Version { get; private set; }

    public Heightfield(int tileWidth, int tileHeight)
    {
        if (tileWidth <= 0 || tileHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(tileWidth), "Heightfield requires positive tile dimensions.");
        }
        VertWidth = tileWidth + 1;
        VertHeight = tileHeight + 1;
        _heights = new short[VertWidth * VertHeight];
    }

    public ReadOnlySpan<short> AsReadOnlySpan() => _heights;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Index(int vx, int vy) => vy * VertWidth + vx;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool InBounds(int vx, int vy) =>
        (uint)vx < (uint)VertWidth && (uint)vy < (uint)VertHeight;

    public short Get(int vx, int vy) => _heights[Index(vx, vy)];

    public void Set(int vx, int vy, short quanta)
    {
        var clamped = Clamp(quanta);
        var idx = Index(vx, vy);
        if (_heights[idx] != clamped)
        {
            _heights[idx] = clamped;
            Version++;
        }
    }

    public void Fill(short quanta)
    {
        var clamped = Clamp(quanta);
        Array.Fill(_heights, clamped);
        Version++;
    }

    // Bulk writers (e.g. generators) call this after a sweep to guarantee
    // a Version bump even when individual Set() calls happened to be no-ops.
    public void MarkChanged() => Version++;

    public float MetresAt(int vx, int vy) =>
        Get(vx, vy) * TerrainConstants.VerticalQuantumMetres;

    private static short Clamp(short quanta)
    {
        if (quanta < TerrainConstants.MinQuanta)
        {
            return TerrainConstants.MinQuanta;
        }
        if (quanta > TerrainConstants.MaxQuanta)
        {
            return TerrainConstants.MaxQuanta;
        }
        return quanta;
    }
}
