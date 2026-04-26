using CowColonySim.Sim.Terrain;

namespace CowColonySim.Sim.Pathfinding;

// Walkability + step cost adapter over a Heightfield. Tile (tx, ty) covers
// the four shared corner samples; "tile height" = average of those four.
// A step is walkable when the height delta in quanta is within MaxStepQuanta
// and (for diagonals) both cardinal neighbours are also walkable, so we
// don't squeeze through impassable cliff corners.
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

    public bool CanStep(TileCoord from, TileCoord to)
    {
        if (!InBounds(from) || !InBounds(to)) return false;
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        if (Math.Abs(dx) > 1 || Math.Abs(dy) > 1 || (dx == 0 && dy == 0)) return false;

        var dh = Math.Abs(CenterQuanta(to) - CenterQuanta(from));
        if (dh > MaxStepQuanta) return false;

        if (dx != 0 && dy != 0)
        {
            var sideA = new TileCoord(from.X + dx, from.Y);
            var sideB = new TileCoord(from.X, from.Y + dy);
            if (Math.Abs(CenterQuanta(sideA) - CenterQuanta(from)) > MaxStepQuanta) return false;
            if (Math.Abs(CenterQuanta(sideB) - CenterQuanta(from)) > MaxStepQuanta) return false;
        }
        return true;
    }

    public float StepCost(TileCoord from, TileCoord to)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var planar = (dx != 0 && dy != 0) ? MathF.Sqrt(2f) : 1f;
        var slope = Math.Abs(CenterQuanta(to) - CenterQuanta(from));
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

    private float CenterQuanta(TileCoord t)
    {
        var a = _field.Get(t.X, t.Y);
        var b = _field.Get(t.X + 1, t.Y);
        var c = _field.Get(t.X, t.Y + 1);
        var d = _field.Get(t.X + 1, t.Y + 1);
        return (a + b + c + d) * 0.25f;
    }
}
