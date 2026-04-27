using Friflo.Engine.ECS;

namespace CowColonySim.Sim.World.Components;

// A built blueprint occupying a tile. DefId resolves against
// BlueprintCatalog so the renderer + future systems (heat, FOV, fire)
// read the static def. Walls block pathfinding via HeightGrid; this
// component just tags ownership and carries per-instance orientation.
public struct Structure : IComponent
{
    public string DefId;
    public int Rotation;
    public int BaseLayer;

    public Structure() { DefId = string.Empty; }
}
