using CowColonySim.Sim.Terrain;

namespace CowColonySim.Sim.Pathfinding;

// Walkability + step cost adapter over a Heightfield. Tile (tx, ty) covers
// the four shared corner samples; "tile height" = average of those four.
// A step is walkable when the height delta in quanta is within MaxStepQuanta
// and (for diagonals) both cardinal neighbours are also walkable, so we
// don't squeeze through impassable cliff corners.
//
// Z (floor layer) is derived from the heightfield: Z = round(centerQuanta/2)
// so a 1.5 m vertical step = 1 layer = 2 quanta. On a single-floor world
// every (x, y) maps to one Z; later phases will allow multiple Z per (x, y)
// for stairs/ramps and the A* graph will expand across the new transitions.
//
// Pure-data immutable view: thread-safe to share across A* workers as long
// as the underlying Heightfield isn't being mutated concurrently.
public sealed class HeightGrid
{
    private const int MaxStepQuanta = 2;
    private const float SlopeCostPerQuanta = 0.4f;

    private readonly Heightfield _field;

    public int Width { get; }
    public int Height { get; }

    public HeightGrid(Heightfield field)
    {
        _field = field;
        Width = field.VertWidth - 1;
        Height = field.VertHeight - 1;
    }

    public bool InBounds(TileCoord t) =>
        (uint)t.X < (uint)Width && (uint)t.Y < (uint)Height;

    // Build a 3D coord at (x, y) with floor Z derived from the heightfield.
    public TileCoord At(int x, int y) => new(x, y, FloorLayer(x, y));

    public int FloorLayer(int x, int y) =>
        (int)MathF.Round(CenterQuanta(x, y) * 0.5f);

    public bool CanStep(TileCoord from, TileCoord to)
    {
        if (!InBounds(from) || !InBounds(to)) return false;
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        if (Math.Abs(dx) > 1 || Math.Abs(dy) > 1 || (dx == 0 && dy == 0)) return false;

        var dh = Math.Abs(CenterQuanta(to.X, to.Y) - CenterQuanta(from.X, from.Y));
        if (dh > MaxStepQuanta) return false;

        if (dx != 0 && dy != 0)
        {
            if (Math.Abs(CenterQuanta(from.X + dx, from.Y) - CenterQuanta(from.X, from.Y)) > MaxStepQuanta) return false;
            if (Math.Abs(CenterQuanta(from.X, from.Y + dy) - CenterQuanta(from.X, from.Y)) > MaxStepQuanta) return false;
        }
        return true;
    }

    public float StepCost(TileCoord from, TileCoord to)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var planar = (dx != 0 && dy != 0) ? MathF.Sqrt(2f) : 1f;
        var slope = Math.Abs(CenterQuanta(to.X, to.Y) - CenterQuanta(from.X, from.Y));
        return planar + slope * SlopeCostPerQuanta;
    }

    public static float OctileHeuristic(TileCoord a, TileCoord b)
    {
        var dx = Math.Abs(a.X - b.X);
        var dy = Math.Abs(a.Y - b.Y);
        var min = Math.Min(dx, dy);
        var max = Math.Max(dx, dy);
        return (max - min) + min * MathF.Sqrt(2f);
    }

    private float CenterQuanta(int x, int y)
    {
        var a = _field.Get(x, y);
        var b = _field.Get(x + 1, y);
        var c = _field.Get(x, y + 1);
        var d = _field.Get(x + 1, y + 1);
        return (a + b + c + d) * 0.25f;
    }
}
