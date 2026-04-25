namespace CowColonySim.Sim.Map;

[Flags]
public enum TileFlags : byte
{
    None = 0,
    HasFloor = 1 << 0,
    HasWall = 1 << 1,
    HasRoof = 1 << 2,
    IsSolidGround = 1 << 3,
    ExposedToSky = 1 << 4,
}

public static class TileFlagsExtensions
{
    public const TileFlags VerticalLightBlockerMask =
        TileFlags.HasFloor | TileFlags.HasRoof | TileFlags.IsSolidGround;

    public const TileFlags HorizontalLightBlockerMask =
        TileFlags.HasWall | TileFlags.IsSolidGround;

    public const TileFlags AnyLightBlockerMask =
        TileFlags.HasFloor | TileFlags.HasWall | TileFlags.HasRoof | TileFlags.IsSolidGround;

    public static bool BlocksVerticalLight(this TileFlags flags) =>
        (flags & VerticalLightBlockerMask) != 0;

    public static bool BlocksHorizontalLight(this TileFlags flags) =>
        (flags & HorizontalLightBlockerMask) != 0;

    public static bool BlocksAnyLight(this TileFlags flags) =>
        (flags & AnyLightBlockerMask) != 0;
}
