using Friflo.Engine.ECS;

namespace CowColonySim.Sim.Lighting;

public struct TileCoord : IComponent
{
    public int X;
    public int Y;
    public int Z;

    public TileCoord(int x, int y, int z) { X = x; Y = y; Z = z; }
}

public struct LightEmitter : IComponent
{
    public byte Intensity;
    public int Radius;

    public LightEmitter(byte intensity, int radius)
    {
        Intensity = intensity;
        Radius = radius;
    }
}
