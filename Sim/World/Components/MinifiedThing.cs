using Friflo.Engine.ECS;

namespace CowColonySim.Sim.World.Components;

// Sidecar to Item (Kind=Minified). Captures everything needed to
// rebuild the original Structure when the item is consumed by a
// matching blueprint: DefId for catalog lookup, Rotation/BaseLayer
// for the original pose (advisory; reinstall pose comes from the
// new ghost), and a placeholder for future per-instance settings.
//
// "killed minifying last time" — past iteration lost custom state on
// uninstall. Plumbing exists here so settings-bearing structures can
// extend this struct without rewriting the haul/build pipeline.
public struct MinifiedThing : IComponent
{
    public string DefId;
    public int Rotation;
    public int BaseLayer;

    public MinifiedThing() { DefId = string.Empty; }
}
