using Friflo.Engine.ECS;

namespace CowColonySim.Sim.World.Components;

// Static world resource that satisfies one need kind. While a colonist
// sits on the same tile, JobSystem refills that need at SatisfyPerSec.
public struct NeedSpot : IComponent
{
    public NeedKind Kind;
    public float SatisfyPerSec;
}
