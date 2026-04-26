namespace CowColonySim.Sim.Pathfinding;

public readonly record struct TileCoord(int X, int Y)
{
    public override string ToString() => $"({X},{Y})";
}
