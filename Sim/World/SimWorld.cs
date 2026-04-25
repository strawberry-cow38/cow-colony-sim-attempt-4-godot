using Friflo.Engine.ECS;

namespace CowColonySim.Sim.World;

public sealed class SimWorld
{
    public EntityStore Store { get; }

    public SimWorld()
    {
        Store = new EntityStore();
    }
}
