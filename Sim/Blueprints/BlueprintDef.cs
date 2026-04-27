using System.Collections.Generic;

namespace CowColonySim.Sim.Blueprints;

// Static, hand-authored blueprint description. Lives in BlueprintCatalog
// keyed by Id. Per-instance state (origin, rotation, build progress)
// lives on the BlueprintGhost component.
//
// FootprintW/H are in tiles, expressed at rotation = 0. Rotation rotates
// the footprint and any FootprintRequirement offsets together.
public sealed record BlueprintDef(
    string Id,
    string DisplayName,
    BlueprintCategory Category,
    PlacementMode Placement,
    int FootprintW,
    int FootprintH,
    bool Rotatable,
    IReadOnlyList<FootprintRequirement> Requirements,
    float HeightMeters = 1.5f,
    IReadOnlyList<MaterialCost>? Materials = null)
{
    // Height converted to 0.75 m vertical quanta — matches the terrain
    // quantum + the build-layer step. Quarter wall = 1, half wall = 2,
    // full wall = 4.
    public int HeightQuanta => System.Math.Max(1, (int)System.MathF.Round(HeightMeters / 0.75f));

    public IReadOnlyList<MaterialCost> MaterialsOrEmpty =>
        Materials ?? System.Array.Empty<MaterialCost>();
}
