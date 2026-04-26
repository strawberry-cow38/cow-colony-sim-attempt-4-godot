namespace CowColonySim.Sim.Blueprints;

// Optional placement requirements stacked on top of the default
// "level + unobstructed footprint" check used by PlacementMode.Footprint.
//   InteractionSpot = one tile adjacent to the footprint (offset from
//                     the footprint origin) must be walkable; that's
//                     where colonists stand to use the workstation.
//   VentSide        = one footprint edge must face an unobstructed
//                     tile (ac/exhaust units).
public readonly record struct FootprintRequirement(
    FootprintRequirementKind Kind,
    int OffsetX,
    int OffsetY);

public enum FootprintRequirementKind
{
    InteractionSpot = 0,
    VentSide = 1,
}
