namespace CowColonySim.Sim.Snapshots;

// One pine tree on the map. Game side renders pine.glb at TileX/TileY.
// Health drops as colonists chop; entity is deleted when it hits 0.
// VariantSeed lets the renderer pick a random rotation/scale per tree
// so the forest doesn't look stamped from one transform.
public readonly record struct TreeView(
    int EntityId,
    int TileX,
    int TileY,
    int Health,
    uint VariantSeed,
    bool BeingChopped,
    int HitCount);
