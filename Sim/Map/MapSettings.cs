namespace CowColonySim.Sim.Map;

// Phase target: 256x256 tiles per cell with a tall vertical column from
// z=-64 (deep underground) to z=64 (sky towers). 129 z-layers total.
// Override per-test for smaller cases.
public sealed record MapSettings(
    int Width = 256,
    int Height = 256,
    int MinZ = -64,
    int MaxZ = 64,
    int Seed = 0)
{
    public int Depth => MaxZ - MinZ + 1;
}
