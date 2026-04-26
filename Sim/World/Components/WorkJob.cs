using CowColonySim.Sim.Designations;
using CowColonySim.Sim.Items;
using Friflo.Engine.ECS;

namespace CowColonySim.Sim.World.Components;

// Player-designated work assignment. Separate from Job (need fulfillment)
// so needs always preempt work — a hungry colonist drops the chop and
// goes to eat. ChopJobSystem clears Active when the target is gone.
public struct WorkJob : IComponent
{
    public bool Active;
    public WorkKind Kind;
    public int TargetTileX;
    public int TargetTileY;
    public int TargetEntityId;
    // Sub-tick progress accumulator. Integer-health work (chop, mine) ticks
    // at ChopRatePerSec but fixed-step is 60 Hz, so we accumulate fractions
    // here and subtract a whole point each time it crosses 1.
    public float Progress;
    // Player force-prioritized this specific assignment via the context
    // menu. ChopJobSystem.TryAssignChop won't repoint a forced WorkJob to
    // a closer tree on the next tick — only target invalid clears it.
    public bool Forced;

    // Haul-only state. Two phases: walk to the source item, then walk to
    // the drop tile. Carrying flips true once the colonist reaches the
    // pickup tile and the item entity is consumed. CarryKind/Count buffer
    // the payload so we can deposit (or restore on cancel).
    public bool Carrying;
    public ItemKind CarryKind;
    public int CarryCount;
    public int DropTileX;
    public int DropTileY;
}
