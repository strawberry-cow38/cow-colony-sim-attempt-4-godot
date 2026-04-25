namespace CowColonySim.Sim.Terrain;

public static class TerrainConstants
{
    // Vertical resolution: terrain vertices snap to 0.75m steps.
    // Two vertical quanta per 1.5m tile, so a wall is exactly 2 quanta tall.
    public const float VerticalQuantumMetres = 0.75f;

    // Range for the quantised height value. With short backing, this is plenty;
    // pick a hard limit that comfortably contains z = -64..+64 tile range.
    public const short MinQuanta = -512;
    public const short MaxQuanta = 512;
}
