namespace CowColonySim.Sim.Climate;

// Thread-safe holder for the latest ClimateSnapshot. SimThread writes,
// snapshot builders read.
public sealed class ClimateState
{
    private ClimateSnapshot _current = ClimateSnapshot.Empty;

    public ClimateSnapshot Current => Volatile.Read(ref _current);

    public void Publish(ClimateSnapshot next) => Volatile.Write(ref _current, next);
}
