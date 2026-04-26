using CowColonySim.Sim.Designations;
using CowColonySim.Sim.Items;
using CowColonySim.Sim.World.Components;

namespace CowColonySim.Sim.Snapshots;

// Per-colonist row in the snapshot. EntityId lets the game side address
// commands back to the same entity. Ground-plane metres only — vertical
// (Y in Godot) is sampled from the heightfield by the renderer.
public readonly record struct ColonistView(
    int EntityId,
    float MetersX,
    float MetersY,
    float Hunger,
    float Thirst,
    float Energy,
    bool JobActive,
    NeedKind JobKind,
    bool WorkActive,
    WorkKind WorkKind,
    bool Carrying,
    ItemKind CarryKind,
    int CarryCount);
