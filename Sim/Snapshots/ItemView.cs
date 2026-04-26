using CowColonySim.Sim.Items;

namespace CowColonySim.Sim.Snapshots;

// Immutable per-frame view of one ground stack. Renderer reads Count
// + Capacity to pick the visual tier.
public readonly record struct ItemView(
    int EntityId,
    ItemKind Kind,
    int Count,
    int Capacity,
    int TileX,
    int TileY,
    bool Forbidden);
