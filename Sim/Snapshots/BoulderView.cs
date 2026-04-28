namespace CowColonySim.Sim.Snapshots;

// One boulder on the map. Renderer picks a mesh by Variant and applies
// rotation+scale jitter from VariantSeed. BeingMined drives a wobble so
// the player can see which rocks are actively worked.
public readonly record struct BoulderView(
    int EntityId,
    int TileX,
    int TileY,
    int Health,
    uint VariantSeed,
    int Variant,
    bool BeingMined,
    int HitCount);
