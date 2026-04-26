namespace CowColonySim.Sim.Snapshots;

// Atomic single-slot publisher. SimThread writes; Game reads. Reference
// assignment is atomic on .NET, but Volatile.Read/Write makes the memory
// ordering explicit — no torn reads, no stale value lingering in a CPU
// cache after a publish.
public sealed class SnapshotPublisher
{
    private SimSnapshot _current = SimSnapshot.Empty;

    public SimSnapshot Current => Volatile.Read(ref _current);

    public void Publish(SimSnapshot snapshot)
    {
        Volatile.Write(ref _current, snapshot);
    }
}
