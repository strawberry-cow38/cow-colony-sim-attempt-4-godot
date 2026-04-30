using Friflo.Engine.ECS;

namespace CowColonySim.Sim.World.Components;

public enum BillRepeatMode
{
    // Run the recipe forever; never auto-completes.
    Forever = 0,
    // Run until DoneCount reaches TargetCount, then stop.
    DoX = 1,
    // Run until the world holds at least TargetCount of the recipe's
    // output kind. Counts every ground stack (no stockpile filter yet).
    UntilCount = 2,
}

// One queued recipe entry on a workstation. Suspending pauses without
// removing. WorkProgress accumulates across colonist visits if we ever
// allow split work; today the cook tick from 0 to recipe.WorkSeconds in
// one sit, so this just resets when a fresh run starts.
public struct Bill
{
    public string RecipeId;
    public BillRepeatMode RepeatMode;
    public int TargetCount;
    public bool Suspended;
    public int DoneCount;
    public float WorkProgress;
}

// Component on workstation Structure entities. Friflo struct + List ref —
// matches Inventory's pattern.
public struct Bills : IComponent
{
    public List<Bill> Entries;
    // Bumped every mutation so the UI can detect changes without diffing
    // the list. Snapshot publisher reads it to keep stove panels live.
    public uint Version;

    public static Bills New() => new() { Entries = new List<Bill>() };
}
