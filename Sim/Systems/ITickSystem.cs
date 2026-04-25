namespace CowColonySim.Sim.Systems;

public interface ITickSystem
{
    string Name { get; }
    void Tick(in TickContext ctx);
}

public readonly struct TickContext
{
    public readonly long TickNumber;
    public readonly double FixedDelta;

    public TickContext(long tickNumber, double fixedDelta)
    {
        TickNumber = tickNumber;
        FixedDelta = fixedDelta;
    }
}
