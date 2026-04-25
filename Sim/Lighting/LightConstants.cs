namespace CowColonySim.Sim.Lighting;

public static class LightConstants
{
    public const byte Max = 255;
    public const byte ArtificialMax = 127;
    public const byte Off = 0;

    public static byte Percent(byte value) => (byte)((value * 100) / Max);
}
