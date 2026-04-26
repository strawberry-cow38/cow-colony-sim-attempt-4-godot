namespace CowColonySim.Sim.Snapshots;

// Immutable end-of-tick view of the simulation. Game-side code only ever
// reads these — never reaches into the EntityStore directly.
public sealed record SimSnapshot(
    long TickNumber,
    double ElapsedSeconds,
    int EntityCount,
    IReadOnlyList<ColonistView> Colonists)
{
    public static SimSnapshot Empty { get; } =
        new(0, 0.0, 0, Array.Empty<ColonistView>());
}
