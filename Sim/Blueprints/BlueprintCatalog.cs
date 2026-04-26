using System.Collections.Generic;

namespace CowColonySim.Sim.Blueprints;

// Static lookup of every BlueprintDef in the game. Build menu reads
// this to populate categories; placement code reads it to validate
// footprints. Dummy entries cover one example per placement mode so
// the rest of the framework has something concrete to bind to.
public static class BlueprintCatalog
{
    private static readonly Dictionary<string, BlueprintDef> _defs = Build();

    public static IReadOnlyDictionary<string, BlueprintDef> All => _defs;

    public static BlueprintDef Get(string id) => _defs[id];

    public static bool TryGet(string id, out BlueprintDef? def) => _defs.TryGetValue(id, out def);

    private static Dictionary<string, BlueprintDef> Build()
    {
        var defs = new[]
        {
            new BlueprintDef(
                Id: "structure.wall",
                DisplayName: "Wall",
                Category: BlueprintCategory.Structure,
                Placement: PlacementMode.LineDrag,
                FootprintW: 1, FootprintH: 1,
                Rotatable: false,
                Requirements: System.Array.Empty<FootprintRequirement>(),
                HeightMeters: 3.0f),

            new BlueprintDef(
                Id: "structure.wall_half",
                DisplayName: "Half Wall",
                Category: BlueprintCategory.Structure,
                Placement: PlacementMode.LineDrag,
                FootprintW: 1, FootprintH: 1,
                Rotatable: false,
                Requirements: System.Array.Empty<FootprintRequirement>(),
                HeightMeters: 1.5f),

            new BlueprintDef(
                Id: "structure.wall_quarter",
                DisplayName: "Quarter Wall",
                Category: BlueprintCategory.Structure,
                Placement: PlacementMode.LineDrag,
                FootprintW: 1, FootprintH: 1,
                Rotatable: false,
                Requirements: System.Array.Empty<FootprintRequirement>(),
                HeightMeters: 0.75f),

            new BlueprintDef(
                Id: "structure.door",
                DisplayName: "Door",
                Category: BlueprintCategory.Structure,
                Placement: PlacementMode.Single,
                FootprintW: 1, FootprintH: 1,
                Rotatable: true,
                Requirements: System.Array.Empty<FootprintRequirement>()),

            new BlueprintDef(
                Id: "workstation.crafting_table",
                DisplayName: "Crafting Table",
                Category: BlueprintCategory.Workstation,
                Placement: PlacementMode.Footprint,
                FootprintW: 2, FootprintH: 1,
                Rotatable: true,
                Requirements: new[]
                {
                    new FootprintRequirement(FootprintRequirementKind.InteractionSpot, 0, 1),
                }),

            new BlueprintDef(
                Id: "utility.ac_unit",
                DisplayName: "AC Unit",
                Category: BlueprintCategory.Utility,
                Placement: PlacementMode.Footprint,
                FootprintW: 1, FootprintH: 1,
                Rotatable: true,
                Requirements: new[]
                {
                    new FootprintRequirement(FootprintRequirementKind.VentSide, 0, -1),
                }),
        };

        var dict = new Dictionary<string, BlueprintDef>(defs.Length);
        foreach (var d in defs) dict[d.Id] = d;
        return dict;
    }
}
