namespace CowColonySim.Sim.Designations;

// Player-designated work a colonist can pick up. Distinct from NeedKind
// (which drives self-care like eat/drink/sleep). One-of-a-kind today —
// mining, hauling, harvesting, building all map onto this enum later.
public enum WorkKind
{
    None = 0,
    ChopTree = 1,
    HaulItem = 2,
}
