using CowColonySim.Sim.Items;

namespace CowColonySim.Sim.Snapshots;

// Immutable per-frame view of one ground stack. Renderer reads Count
// + Capacity to pick the visual tier. MinifiedDefId is non-null only
// when Kind == Minified; carries the wrapped structure's defId for
// labels and reinstall matching.
public readonly record struct ItemView(
    int EntityId,
    ItemKind Kind,
    int Count,
    int Capacity,
    int TileX,
    int TileY,
    bool Forbidden,
    string? MinifiedDefId = null,
    int MinifiedRotation = 0);
