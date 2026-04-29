using CowColonySim.Sim.World;
using CowColonySim.Sim.World.Components;

namespace CowColonySim.Sim.Snapshots;

// Per-PowerNode row. Position is meters world XY at the node's tile (Z
// resolved by renderer from heightfield). Game uses this to place pylon
// meshes and to identify the served-by pylon for a generator/lamp UI hover.
public readonly record struct PowerNodeView(
    int EntityId,
    PowerNodeKind Kind,
    int GridId,
    float MetersX,
    float MetersY,
    int TileX,
    int TileY,
    float SupplyW,
    float DemandW,
    bool IsActive,
    bool IsPowered,
    int ServedByPylonId);

// One cable. IsHop = pylon→pylon span; otherwise = device→pylon service tap.
// Renderer draws a sagging catenary between the two world XY positions.
public readonly record struct PowerEdgeView(
    int FromEntityId,
    int ToEntityId,
    float FromMetersX,
    float FromMetersY,
    float ToMetersX,
    float ToMetersY,
    bool IsHop,
    int GridId);

// Aggregate stats per electrically-connected island. UI reads this to show
// supply/demand/status without re-walking nodes on the game side.
public readonly record struct PowerGridView(
    int Id,
    float TotalSupplyW,
    float TotalDemandW,
    GridStatus Status,
    int PylonCount,
    int SourceCount,
    int SinkCount);
