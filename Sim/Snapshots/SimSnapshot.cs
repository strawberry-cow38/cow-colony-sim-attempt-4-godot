namespace CowColonySim.Sim.Snapshots;

// Immutable end-of-tick view of the simulation. Game-side code only ever
// reads these — never reaches into the EntityStore directly. Pre-pre-game:
// nothing to snapshot besides the clock; expand as systems land.
public sealed record SimSnapshot(long TickNumber, double ElapsedSeconds, int EntityCount)
{
    public static SimSnapshot Empty { get; } = new(0, 0.0, 0);
}
