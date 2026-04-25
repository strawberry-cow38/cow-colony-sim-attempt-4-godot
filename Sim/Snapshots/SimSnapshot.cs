namespace CowColonySim.Sim.Snapshots;

public sealed record SimSnapshot(long TickNumber, double SimTimeSeconds)
{
    public static SimSnapshot Empty { get; } = new(0, 0.0);
}
