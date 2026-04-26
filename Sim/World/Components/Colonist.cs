using Friflo.Engine.ECS;

namespace CowColonySim.Sim.World.Components;

// Marker for colonist entities. Movement state lives on PathFollower.
public struct Colonist : IComponent
{
    public uint Rng;
}
