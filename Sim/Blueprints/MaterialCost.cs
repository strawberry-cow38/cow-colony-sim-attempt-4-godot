using CowColonySim.Sim.Items;

namespace CowColonySim.Sim.Blueprints;

// One material requirement for a blueprint. Walls list a single entry
// (5 wood); the data shape is a list so future recipes that need wood
// + stone slot in without breaking callers.
public readonly record struct MaterialCost(ItemKind Kind, int Count);
