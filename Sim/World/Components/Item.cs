using CowColonySim.Sim.Items;
using Friflo.Engine.ECS;

namespace CowColonySim.Sim.World.Components;

// A stack of one item kind on a tile. Visual size tier comes from
// Count / Capacity (renderer maps the fraction to tier 0/1/2). When
// AddOrMergeItem hits an existing stack with room left it bumps Count
// instead of spawning a new entity, so a chopped tree's yield collapses
// into one stack per tile.
public struct Item : IComponent
{
    public ItemKind Kind;
    public int Count;
    public int Capacity;
    public bool Forbidden;
}
