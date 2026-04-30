using CowColonySim.Sim.World.Components;

namespace CowColonySim.Sim.Snapshots;

public readonly record struct BillView(
    string RecipeId,
    BillRepeatMode RepeatMode,
    int TargetCount,
    bool Suspended,
    int DoneCount);
