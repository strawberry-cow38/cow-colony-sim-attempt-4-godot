using Friflo.Engine.ECS;

namespace CowColonySim.Sim.World.Components;

// A mineable boulder rooted on the terrain. Health is in mine-points; each
// mine tick subtracts DamagePerHit. VariantSeed seeds renderer rotation +
// scale + mesh-pick so a field of boulders doesn't look stamped. Variant
// chooses one of the boulder/stone meshes the renderer loads.
public struct Boulder : IComponent
{
    public int Health;
    public uint VariantSeed;
    // Increments once per discrete mine hit so renderer/audio can react.
    public int HitCount;
    // 0..N-1 picks which boulder/stone mesh bucket draws this entity.
    public int Variant;
}
