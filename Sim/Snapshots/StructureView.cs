namespace CowColonySim.Sim.Snapshots;

// One built structure on a tile. Game side resolves DefId against
// BlueprintCatalog for footprint + height. Bills is non-empty only on
// workstations that carry a Bills component.
public readonly record struct StructureView(
    int EntityId,
    string DefId,
    int TileX,
    int TileY,
    int Rotation,
    int BaseLayer,
    IReadOnlyList<BillView> Bills);
