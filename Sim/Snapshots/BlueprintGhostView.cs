namespace CowColonySim.Sim.Snapshots;

// One placed blueprint ghost waiting to be built. Game side resolves
// DefId against BlueprintCatalog to draw the footprint outline.
public readonly record struct BlueprintGhostView(
    int EntityId,
    string DefId,
    int OriginTileX,
    int OriginTileY,
    int Rotation,
    float BuildProgress);
