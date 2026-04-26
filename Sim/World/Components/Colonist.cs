using Friflo.Engine.ECS;

namespace CowColonySim.Sim.World.Components;

// Marker + per-entity wander state. Velocity is metres per second on the
// ground plane (TilePosition.MetersX / MetersY axes). Rng is xorshift state
// stamped at spawn so each colonist re-rolls direction independently.
public struct Colonist : IComponent
{
    public uint Rng;
    public float VelMpsX;
    public float VelMpsY;
    public long NextRerollTick;
}
