using Friflo.Engine.ECS;

namespace CowColonySim.Sim.World.Components;

// Role of a PowerNode on the electricity graph.
//   Pylon    — relay only. No supply/demand of its own. Connects to other
//              pylons within cable-hop range and serves consumers/sources
//              within service-sphere radius.
//   Source   — generator. SupplyW is what it pushes into the grid when
//              IsActive. Auto-cabled to nearest pylon within service range.
//   Sink     — consumer (lamp, etc). DemandW is what it pulls. Auto-cabled
//              to nearest pylon within service range. IsPowered set by
//              PowerSystem when its grid has supply >= demand.
public enum PowerNodeKind : byte
{
    Pylon = 0,
    Source = 1,
    Sink = 2,
}

// Per-entity electricity component. Source.SupplyW is the watts the
// generator pushes when IsActive. Sink.DemandW is the watts a lamp draws.
// GridId is set by PowerSystem each topology pass; -1 means "unattached".
// ServedByPylonId is the pylon a Source/Sink is currently cabled to (entity
// id), or 0 if floating. Pylons leave it at 0.
public struct PowerNode : IComponent
{
    public PowerNodeKind Kind;
    public float SupplyW;
    public float DemandW;
    public bool IsActive;
    public int GridId;
    public int ServedByPylonId;
    public bool IsPowered;
}
