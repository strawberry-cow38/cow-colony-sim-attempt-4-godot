using Friflo.Engine.ECS;

namespace CowColonySim.Sim.World.Components;

// Active assignment for a colonist. When Active is false the colonist
// is idle (and falls through to WanderSystem for filler movement).
public struct Job : IComponent
{
    public bool Active;
    public NeedKind NeedKind;
    public int TargetTileX;
    public int TargetTileY;
}
