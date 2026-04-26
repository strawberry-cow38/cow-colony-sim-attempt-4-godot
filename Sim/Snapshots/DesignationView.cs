using CowColonySim.Sim.Designations;

namespace CowColonySim.Sim.Snapshots;

// One designated target. Game side draws the kind icon over the tile.
public readonly record struct DesignationView(
    int EntityId,
    DesignationKind Kind,
    int TileX,
    int TileY);
