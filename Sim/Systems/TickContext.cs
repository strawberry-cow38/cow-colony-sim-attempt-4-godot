namespace CowColonySim.Sim.Systems;

public readonly record struct TickContext(long TickNumber, double FixedDeltaSeconds);
