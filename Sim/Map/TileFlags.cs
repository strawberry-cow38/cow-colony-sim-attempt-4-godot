namespace CowColonySim.Sim.Map;

[Flags]
public enum TileFlags : byte
{
    None = 0,
    Solid = 1 << 0,
    Walkable = 1 << 1,
    ExposedToSky = 1 << 2,
    Water = 1 << 3,
}
