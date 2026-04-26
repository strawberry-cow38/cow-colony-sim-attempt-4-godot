namespace CowColonySim.Sim.Terrain;

public static class TerrainConstants
{
    // 1.5 m horizontal tile, 0.75 m vertical quantum → 2 vertical steps per
    // tile width, AoE2-flavoured blocky relief.
    public const float VerticalQuantumMetres = 0.75f;

    // z range of -64..64 tile-layers = -96 m .. 96 m. At 0.75 m quanta that's
    // -128 .. 128 quanta. Pad a little in case worldgen wants to overshoot
    // before clamp-on-write.
    public const short MinQuanta = -256;
    public const short MaxQuanta = 256;
}
