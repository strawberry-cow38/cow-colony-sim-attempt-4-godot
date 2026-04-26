namespace CowColonySim.Sim.Pathfinding;

// 3D tile coordinate. X/Y is the planar tile, Z is the floor layer
// (1 layer = 1 tile = 1.5 m vertical = 2 quanta). On a single-floor
// world Z is just the heightfield-derived floor; ramps/stairs in
// later phases will produce multiple layers per (X, Y) and the A*
// graph will expand across them.
public readonly record struct TileCoord(int X, int Y, int Z)
{
    public TileCoord(int x, int y) : this(x, y, 0) { }

    public override string ToString() => $"({X},{Y},{Z})";
}
