using Friflo.Engine.ECS;

namespace CowColonySim.Sim.World.Components;

// A point source that contributes to the per-tile light grid. Range is
// in tiles; intensity at the emitter's tile is Intensity (clamped to
// 0.5 when LightingSystem applies it — player-built lights cap there).
// Falloff is linear: contribution = intensity * (1 - distance/range).
public struct LightEmitter : IComponent
{
    public float RangeTiles;
    public float Intensity;
}
