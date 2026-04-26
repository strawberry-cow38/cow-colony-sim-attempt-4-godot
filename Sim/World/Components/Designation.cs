using CowColonySim.Sim.Designations;
using Friflo.Engine.ECS;

namespace CowColonySim.Sim.World.Components;

// Tag stamped on an entity that the player has designated for work.
// JobSystem reads these and assigns matching colonists. Removing the
// component cancels the designation.
public struct Designation : IComponent
{
    public DesignationKind Kind;
}
