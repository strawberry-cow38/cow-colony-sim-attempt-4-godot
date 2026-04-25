using System.Runtime.CompilerServices;

namespace CowColonySim.Sim.Terrain;

// Vertex grid sitting on tile corners: a (Width+1) x (Height+1) array of
// height samples, each measured in 0.75m quanta from world origin.
// SoA: a flat short[]. Read/write is engine-agnostic; Godot consumes a copy.
public sealed class Heightfield
{
    private readonly short[] _heights;

    public int VertWidth { get; }
    public int VertHeight { get; }
    public int Version { get; private set; }

    public Heightfield(int tileWidth, int tileHeight)
    {
        VertWidth = tileWidth + 1;
        VertHeight = tileHeight + 1;
        _heights = new short[VertWidth * VertHeight];
    }

    public ReadOnlySpan<short> AsReadOnlySpan() => _heights;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Index(int vx, int vy) => vy * VertWidth + vx;

    public bool InBounds(int vx, int vy) =>
        (uint)vx < (uint)VertWidth && (uint)vy < (uint)VertHeight;

    public short Get(int vx, int vy) => _heights[Index(vx, vy)];

    public void Set(int vx, int vy, short quanta)
    {
        var clamped = quanta < TerrainConstants.MinQuanta ? TerrainConstants.MinQuanta
                    : quanta > TerrainConstants.MaxQuanta ? TerrainConstants.MaxQuanta
                    : quanta;
        var idx = Index(vx, vy);
        if (_heights[idx] != clamped)
        {
            _heights[idx] = clamped;
            Version++;
        }
    }

    public void Fill(short quanta)
    {
        var clamped = quanta < TerrainConstants.MinQuanta ? TerrainConstants.MinQuanta
                    : quanta > TerrainConstants.MaxQuanta ? TerrainConstants.MaxQuanta
                    : quanta;
        Array.Fill(_heights, clamped);
        Version++;
    }

    public float MetresAt(int vx, int vy) =>
        Get(vx, vy) * TerrainConstants.VerticalQuantumMetres;
}
