namespace CowColonySim.Sim.Designations;

// Work-designator categories. Each kind is a job the player has marked
// for any matching target inside a designator rect (chop trees in the
// rect, mine rocks in the rect, etc.). The designator itself is not
// an in-world entity — it lives only as a Designation tag stamped on
// the targets in its area.
public enum DesignationKind
{
    ChopTree = 0,
    Mine = 1,
    Harvest = 2,
    CutPlant = 3,
    Sow = 4,
    // Player marked this structure tile for uninstall — walk a colonist
    // over, tick progress, then swap the structure to a minified package.
    Uninstall = 5,
    // Same flow as Uninstall but yields half the materials back instead.
    Deconstruct = 6,
}
