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
    private int _dirtyMinX, _dirtyMinY, _dirtyMaxX, _dirtyMaxY;
    private bool _hasDirty;

    public int VertWidth { get; }
    public int VertHeight { get; }
    public int Version { get; private set; }
    public bool HasDirtyRegion => _hasDirty;

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
            ExpandDirty(vx, vy);
        }
    }

    public void Fill(short quanta)
    {
        var clamped = Clamp(quanta);
        Array.Fill(_heights, clamped);
        Version++;
        ExpandDirty(0, 0);
        ExpandDirty(VertWidth - 1, VertHeight - 1);
    }

    // Bulk writers (e.g. generators) call this after a sweep to guarantee
    // a Version bump even when individual Set() calls happened to be no-ops.
    public void MarkChanged() => Version++;

    // Pulls and clears the smallest axis-aligned vertex bbox that contains
    // every Set() since the last consume. Game-side terrain + nav rebuilds
    // touch only this region. Returns false (no out values) when nothing
    // has changed since the last call. Bbox is inclusive on both ends.
    public bool TryConsumeDirtyRegion(out int minVx, out int minVy, out int maxVx, out int maxVy)
    {
        if (!_hasDirty)
        {
            minVx = minVy = maxVx = maxVy = 0;
            return false;
        }
        minVx = _dirtyMinX;
        minVy = _dirtyMinY;
        maxVx = _dirtyMaxX;
        maxVy = _dirtyMaxY;
        _hasDirty = false;
        return true;
    }

    private void ExpandDirty(int vx, int vy)
    {
        if (!_hasDirty)
        {
            _dirtyMinX = _dirtyMaxX = vx;
            _dirtyMinY = _dirtyMaxY = vy;
            _hasDirty = true;
            return;
        }
        if (vx < _dirtyMinX) _dirtyMinX = vx;
        else if (vx > _dirtyMaxX) _dirtyMaxX = vx;
        if (vy < _dirtyMinY) _dirtyMinY = vy;
        else if (vy > _dirtyMaxY) _dirtyMaxY = vy;
    }

    public float MetresAt(int vx, int vy) =>
        Get(vx, vy) * TerrainConstants.VerticalQuantumMetres;

    // Sample the rendered terrain surface at fractional tile coords.
    // Mirrors TerrainMeshBuilder's triangulation (diagonal TR↔BL) so the
    // height a colonist stands on matches the slope they're rendered on —
    // no snapping when crossing tile/triangle boundaries.
    public float SurfaceMetresAt(float tilesX, float tilesY)
    {
        var maxTx = VertWidth - 2;
        var maxTy = VertHeight - 2;
        if (maxTx < 0 || maxTy < 0)
        {
            return Get(0, 0) * TerrainConstants.VerticalQuantumMetres;
        }
        var tx = (int)MathF.Floor(tilesX);
        var ty = (int)MathF.Floor(tilesY);
        if (tx < 0) tx = 0; else if (tx > maxTx) tx = maxTx;
        if (ty < 0) ty = 0; else if (ty > maxTy) ty = maxTy;
        var u = tilesX - tx;
        var v = tilesY - ty;
        if (u < 0f) u = 0f; else if (u > 1f) u = 1f;
        if (v < 0f) v = 0f; else if (v > 1f) v = 1f;
        float tl = Get(tx, ty);
        float tr = Get(tx + 1, ty);
        float bl = Get(tx, ty + 1);
        float br = Get(tx + 1, ty + 1);
        float q = u + v <= 1f
            ? tl + u * (tr - tl) + v * (bl - tl)
            : br + (1f - u) * (bl - br) + (1f - v) * (tr - br);
        return q * TerrainConstants.VerticalQuantumMetres;
    }

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
