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

    // Reserved for future "long task" semantics — needs WILL be allowed
    // to preempt these (think research, art, surgery) since starving in
    // a 6-day surgery isn't the desired ergonomic. Unused for now;
    // every active work today is treated as a short, must-finish task.
    public bool LongTask;

    // Haul-only state. Two phases: walk to the source item, then walk to
    // the drop tile. Carrying flips true once the colonist reaches the
    // pickup tile and the item entity is consumed. CarryKind/Count buffer
    // the payload so we can deposit (or restore on cancel).
    public bool Carrying;
    public ItemKind CarryKind;
    public int CarryCount;
    // Non-empty only when CarryKind == Minified — carries the wrapped
    // structure's defId so the deposit matches a blueprint by id.
    public string? CarryMinifiedDefId;
    public int DropTileX;
    public int DropTileY;
}
