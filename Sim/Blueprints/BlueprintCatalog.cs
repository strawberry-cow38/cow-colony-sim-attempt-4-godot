using System.Collections.Generic;
using CowColonySim.Sim.Items;

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
                HeightMeters: 3.0f,
                Materials: new[] { new MaterialCost(ItemKind.Wood, 5) }),

            new BlueprintDef(
                Id: "structure.wall_half",
                DisplayName: "Half Wall",
                Category: BlueprintCategory.Structure,
                Placement: PlacementMode.LineDrag,
                FootprintW: 1, FootprintH: 1,
                Rotatable: false,
                Requirements: System.Array.Empty<FootprintRequirement>(),
                HeightMeters: 1.5f,
                Materials: new[] { new MaterialCost(ItemKind.Wood, 2) }),

            new BlueprintDef(
                Id: "structure.wall_quarter",
                DisplayName: "Quarter Wall",
                Category: BlueprintCategory.Structure,
                Placement: PlacementMode.LineDrag,
                FootprintW: 1, FootprintH: 1,
                Rotatable: false,
                Requirements: System.Array.Empty<FootprintRequirement>(),
                HeightMeters: 0.75f,
                Materials: new[] { new MaterialCost(ItemKind.Wood, 1) }),

            new BlueprintDef(
                Id: "structure.door",
                DisplayName: "Door",
                Category: BlueprintCategory.Structure,
                Placement: PlacementMode.Single,
                FootprintW: 1, FootprintH: 1,
                Rotatable: true,
                Requirements: System.Array.Empty<FootprintRequirement>(),
                Materials: new[] { new MaterialCost(ItemKind.Wood, 2) }),

            new BlueprintDef(
                Id: "workstation.crafting_table",
                DisplayName: "Crafting Table",
                Category: BlueprintCategory.Workstation,
                Placement: PlacementMode.Footprint,
                FootprintW: 1, FootprintH: 1,
                Rotatable: true,
                Requirements: new[]
                {
                    new FootprintRequirement(FootprintRequirementKind.InteractionSpot, 0, 1),
                },
                Materials: new[] { new MaterialCost(ItemKind.Wood, 8) }),

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
                    new FootprintRequirement(FootprintRequirementKind.VentSide, 0, 1),
                },
                Materials: new[] { new MaterialCost(ItemKind.Wood, 6) }),

            new BlueprintDef(
                Id: "workstation.stove",
                DisplayName: "Stove",
                Category: BlueprintCategory.Workstation,
                Placement: PlacementMode.Footprint,
                FootprintW: 3, FootprintH: 1,
                Rotatable: true,
                Requirements: new[]
                {
                    new FootprintRequirement(FootprintRequirementKind.InteractionSpot, 1, 1),
                },
                Materials: new[] { new MaterialCost(ItemKind.Wood, 14) }),

            new BlueprintDef(
                Id: "furniture.table",
                DisplayName: "Table",
                Category: BlueprintCategory.Furniture,
                Placement: PlacementMode.Footprint,
                FootprintW: 2, FootprintH: 2,
                Rotatable: false,
                Requirements: System.Array.Empty<FootprintRequirement>(),
                HeightMeters: 0.75f,
                Materials: new[] { new MaterialCost(ItemKind.Wood, 12) }),
        };

        var dict = new Dictionary<string, BlueprintDef>(defs.Length);
        foreach (var d in defs) dict[d.Id] = d;
        return dict;
    }
}
