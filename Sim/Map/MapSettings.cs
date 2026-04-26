namespace CowColonySim.Sim.Map;

// Phase target: 256x256 tiles per cell. Z range covers the multi-level
// vertical map. Defaults reflect the pre-game one-cell skeleton; override
// per-test as needed.
public sealed record MapSettings(
    int Width = 256,
    int Height = 256,
    int MinZ = 0,
    int MaxZ = 4,
    int Seed = 0)
{
    public int Depth => MaxZ - MinZ + 1;
}
