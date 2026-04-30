using CowColonySim.Sim.Items;

namespace CowColonySim.Sim.Crafting;

// Static lookup of every RecipeDef in the game. Bills UI reads this
// to populate the per-workstation recipe picker. Mirrors
// BlueprintCatalog / ItemCatalog.
public static class RecipeCatalog
{
    private static readonly Dictionary<string, RecipeDef> _defs = Build();

    public static IReadOnlyDictionary<string, RecipeDef> All => _defs;

    public static RecipeDef Get(string id) => _defs[id];

    public static bool TryGet(string id, out RecipeDef? def) => _defs.TryGetValue(id, out def);

    public static IEnumerable<RecipeDef> ForWorkstation(string defId)
    {
        foreach (var r in _defs.Values)
        {
            for (var i = 0; i < r.AllowedWorkstations.Count; i++)
            {
                if (r.AllowedWorkstations[i] == defId) { yield return r; break; }
            }
        }
    }

    private static Dictionary<string, RecipeDef> Build()
    {
        var defs = new[]
        {
            new RecipeDef(
                Id: "recipe.bread",
                DisplayName: "Bake bread",
                Inputs: new[] { new RecipeIngredient(ItemKind.Wheat, 1) },
                OutputKind: ItemKind.Bread,
                OutputCount: 1,
                WorkSeconds: 5f,
                AllowedWorkstations: new[] { "workstation.stove" }),
        };
        var dict = new Dictionary<string, RecipeDef>(defs.Length);
        foreach (var d in defs) dict[d.Id] = d;
        return dict;
    }
}
