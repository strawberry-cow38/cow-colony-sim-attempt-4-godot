using Friflo.Engine.ECS;

namespace CowColonySim.Sim.World.Components;

// Generic plant component used by both trees and crops. Growth is a
// 0..100 percentage; once it hits 100 the plant is mature and Age
// counts up toward LifespanTicks before withering.
//
// IsTree separates "trees" (chop → wood, ChopJobSystem) from other
// crops (cut/harvest → produce). CropDefId names the variety so the
// growth catalog can pick per-plant grow rates / thresholds.
public struct Plant : IComponent
{
    public float Growth;
    public int Age;
    public int LifespanTicks;
    public int CropDefId;
    public bool IsTree;
}
