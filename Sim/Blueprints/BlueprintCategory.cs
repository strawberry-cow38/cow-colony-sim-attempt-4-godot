namespace CowColonySim.Sim.Blueprints;

// Top-level grouping for the build menu. Drives which tab a blueprint
// shows up under and (via PlacementMode on the def) how it's placed.
public enum BlueprintCategory
{
    Structure = 0,   // walls, doors — building shell
    Furniture = 1,   // beds, tables — single-tile or small footprint
    Workstation = 2, // crafting/work objects with interaction spots
    Utility = 3,     // ac units, vents, lights
}
