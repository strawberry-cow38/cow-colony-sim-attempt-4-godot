namespace CowColonySim.Sim.Snapshots;

// One built structure on a tile. Game side resolves DefId against
// BlueprintCatalog for footprint + height.
public readonly record struct StructureView(
    int EntityId,
    string DefId,
    int TileX,
    int TileY,
    int Rotation,
    int BaseLayer);
