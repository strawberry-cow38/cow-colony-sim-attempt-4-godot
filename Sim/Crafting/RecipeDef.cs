using CowColonySim.Sim.Items;

namespace CowColonySim.Sim.Crafting;

public readonly record struct RecipeIngredient(ItemKind Kind, int Count);

// One craftable thing. Inputs consumed at completion, output spawned
// adjacent to the workstation. WorkSeconds is the wall-clock cook time.
// AllowedWorkstations is the list of blueprint ids whose Bills menu can
// add this recipe (e.g. {"workstation.stove"}).
public sealed record RecipeDef(
    string Id,
    string DisplayName,
    IReadOnlyList<RecipeIngredient> Inputs,
    ItemKind OutputKind,
    int OutputCount,
    float WorkSeconds,
    IReadOnlyList<string> AllowedWorkstations);
