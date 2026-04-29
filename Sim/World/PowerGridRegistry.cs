namespace CowColonySim.Sim.World;

// One electrically-connected island of pylons + their attached sources/sinks.
// PowerSystem rebuilds these each topology pass. GridStatus is plumbed but
// consumers don't gate on it yet — UI reads it for display.
public enum GridStatus : byte
{
    Online = 0,
    Brownout = 1,
    Blackout = 2,
}

public struct PowerGrid
{
    public int Id;
    public float TotalSupplyW;
    public float TotalDemandW;
    public GridStatus Status;
    public int PylonCount;
    public int SourceCount;
    public int SinkCount;
}

// Sim-side resource. Owned by SimWorld (look up via SimWorld.Power). Not an
// IComponent — there's exactly one per world, lives on the world object so
// every system can read or mutate without a per-entity round-trip.
public sealed class PowerGridRegistry
{
    private readonly Dictionary<int, PowerGrid> _grids = new();
    private int _nextGridId = 1;

    public IReadOnlyDictionary<int, PowerGrid> Grids => _grids;

    public int AllocateGridId() => _nextGridId++;

    public void Clear()
    {
        _grids.Clear();
        _nextGridId = 1;
    }

    public void Set(in PowerGrid grid) => _grids[grid.Id] = grid;

    public bool TryGet(int gridId, out PowerGrid grid) => _grids.TryGetValue(gridId, out grid);
}
