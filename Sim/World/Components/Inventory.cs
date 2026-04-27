using CowColonySim.Sim.Items;
using Friflo.Engine.ECS;

namespace CowColonySim.Sim.World.Components;

// One entry in a colonist's pack. Stackables (wood, wheat) merge into
// the same DefId entry; uniques (weapon, clothing) live as separate
// stacks of count 1. Equipped flips for clothing/weapons that the
// colonist is currently wearing/wielding — gear has to enter the
// inventory first, then get equipped from there.
public struct InventoryStack
{
    public string DefId;
    public int Count;
    public bool Equipped;
    // Player force-picked this stack. Auto-haul and auto-construct skip
    // locked stacks — they sit in inventory until the player force-drops
    // them. Cleared on force-drop only.
    public bool Locked;

    public InventoryStack(string defId, int count, bool equipped = false, bool locked = false)
    {
        DefId = defId;
        Count = count;
        Equipped = equipped;
        Locked = locked;
    }
}

// Colonist-side container. Stacks is owned by the component (allocated
// at spawn). Mutate via Inventory.* helpers — cap checks live there so
// every call site enforces weight/bulk consistently.
public struct Inventory : IComponent
{
    public List<InventoryStack> Stacks;

    public static Inventory New() => new() { Stacks = new List<InventoryStack>() };
}
