namespace CowColonySim.Sim.Zones;

// Inclusive tile-space rectangle used by zones and work designators.
// Min <= Max on both axes; callers normalize before constructing.
public readonly record struct TileRect(int MinX, int MinY, int MaxX, int MaxY)
{
    public int Width => MaxX - MinX + 1;
    public int Height => MaxY - MinY + 1;
    public int Area => Width * Height;

    public bool Contains(int tileX, int tileY)
        => tileX >= MinX && tileX <= MaxX && tileY >= MinY && tileY <= MaxY;

    public static TileRect FromCorners(int ax, int ay, int bx, int by)
        => new(System.Math.Min(ax, bx), System.Math.Min(ay, by),
               System.Math.Max(ax, bx), System.Math.Max(ay, by));
}
