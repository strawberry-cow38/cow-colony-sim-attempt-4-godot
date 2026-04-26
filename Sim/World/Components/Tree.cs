using Friflo.Engine.ECS;

namespace CowColonySim.Sim.World.Components;

// A pine tree standing on the terrain. Lives at the colocated
// TilePosition. Health is in chop-points; one chop tick subtracts 1.
// VariantSeed seeds the renderer's per-instance rotation/scale jitter
// so a forest of these doesn't look like cloned stamps.
public struct Tree : IComponent
{
    public int Health;
    public uint VariantSeed;
    // Increments once per discrete chop hit. Renderer/audio side reads it
    // off the snapshot to fire a thwack SFX exactly when health drops.
    public int HitCount;
}
