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
    // Walk to a designated structure, tick progress, then swap the
    // structure to a minified package on the ground.
    Uninstall = 9,
    // Walk to a designated structure, tick progress, then refund half
    // the materials and remove the structure.
    Deconstruct = 10,
    // Walk adjacent to a Mine-designated boulder, tick progress, then
    // delete it and drop a stone stack on the tile.
    Mine = 11,
    // Walk to a workstation's interaction tile and tick a recipe to
    // completion. Active bill on the workstation drives the loop.
    Cook = 12,
    // Walk to a switchable structure (lamp) and flip its LampSwitch.On.
    // Instant on arrival — no progress timer.
    SwitchLamp = 13,
}
