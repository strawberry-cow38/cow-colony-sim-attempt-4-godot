using Friflo.Engine.ECS;

namespace CowColonySim.Sim.World.Components;

// Player-controlled mode. Drafted colonists ignore needs, hauls, and
// auto-jobs; they only follow direct MoveCommand orders. Undrafted
// colonists ignore MoveCommand orders. Toggled via ToggleDraftCommand
// (R-key shortcut on the selection).
public struct Drafted : IComponent
{
    public bool Active;
}
