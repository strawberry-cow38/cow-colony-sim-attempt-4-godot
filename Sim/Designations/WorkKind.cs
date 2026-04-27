namespace CowColonySim.Sim.Designations;

// Player-designated work a colonist can pick up. Distinct from NeedKind
// (which drives self-care like eat/drink/sleep). One-of-a-kind today —
// mining, hauling, harvesting, building all map onto this enum later.
public enum WorkKind
{
    None = 0,
    ChopTree = 1,
    HaulItem = 2,
    CutPlant = 3,
    HarvestPlant = 4,
    Sow = 5,
    HaulToBlueprint = 6,
    Construct = 7,
    // Player force-pick: walk to an item entity, suck it into Inventory
    // with the Locked flag set, then idle. Auto-systems leave the colonist
    // alone after — only ForceDrop releases the stack.
    ForcePickup = 8,
}
