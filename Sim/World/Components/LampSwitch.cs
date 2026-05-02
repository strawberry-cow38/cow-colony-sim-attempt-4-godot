using Friflo.Engine.ECS;

namespace CowColonySim.Sim.World.Components;

// Player-controllable on/off switch for a powered light. Off = PowerSystem
// skips the demand for this sink AND forces IsPowered = false so the
// renderer's gating turns the bulb dark even on an otherwise-online grid.
// Toggled by a colonist via WorkKind.SwitchLamp (instant-on-arrival).
public struct LampSwitch : IComponent
{
    public bool On;
}
