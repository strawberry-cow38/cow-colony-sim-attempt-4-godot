using CowColonySim.Sim.Designations;
using CowColonySim.Sim.Items;
using CowColonySim.Sim.World.Components;

namespace CowColonySim.Sim.Snapshots;

// Per-colonist row in the snapshot. EntityId lets the game side address
// commands back to the same entity. MetersZ carries the colonist's vertical
// position so wall-top + ladder-climb visuals don't snap back to terrain.
public readonly record struct ColonistView(
    int EntityId,
    float MetersX,
    float MetersY,
    float MetersZ,
    float Hunger,
    float Thirst,
    float Energy,
    bool JobActive,
    NeedKind JobKind,
    bool WorkActive,
    WorkKind WorkKind,
    bool Carrying,
    ItemKind CarryKind,
    int CarryCount,
    float CarryWeight,
    float MaxWeight,
    float CarryBulk,
    float MaxBulk,
    IReadOnlyList<InventoryStackView> Inventory,
    bool Drafted,
    // Length = WorkTypes.Count, indexed by (int)WorkType. Same byte
    // contract as the WorkPriorities component: 0 = won't do, 1-8 = priority.
    byte[] WorkPriorities);
