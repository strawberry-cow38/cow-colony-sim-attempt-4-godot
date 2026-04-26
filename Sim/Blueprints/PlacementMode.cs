namespace CowColonySim.Sim.Blueprints;

// How the player places this blueprint.
//   Single   = one click puts down one ghost (doors).
//   LineDrag = click-and-drag paints ghosts along a tile-axis line (walls).
//   Footprint = single placement w/ rotation (R) and validation against
//               level/unobstructed terrain + per-def requirements
//               (interaction spot, vent side).
public enum PlacementMode
{
    Single = 0,
    LineDrag = 1,
    Footprint = 2,
}
