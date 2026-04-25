namespace CowColonySim.Sim.Saving;

public sealed class SaveResource
{
    public int Version { get; init; } = CurrentVersion;
    public long TickNumber { get; init; }
    public string EntityStoreJson { get; init; } = string.Empty;

    public const int CurrentVersion = 1;
}
