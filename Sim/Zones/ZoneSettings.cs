using Friflo.Engine.ECS;

namespace CowColonySim.Sim.Zones;

// Per-ZoneType settings stub. Real fields land when each zone gets
// fleshed out (filter list for stockpiles, crop+growth for farms).
// Kept as separate components so each zone entity only carries the
// settings struct that matches its ZoneType.

public struct StockpileSettings : IComponent
{
    public int Priority;
}

public struct FarmSettings : IComponent
{
    public int CropDefId;
    // Allow auto-stamping CutPlant on plants whose CropDefId doesn't
    // match the farm's selection. Off when the player wants the field
    // to fallow without colonists clearing it.
    public bool AllowSowing;
    // Allow auto-stamping ChopTree (for tree crops) or Harvest (for
    // others) on matching mature plants. Off when the player wants the
    // crop to keep growing past 100% (no early reaping).
    public bool AllowHarvest;

    public FarmSettings()
    {
        CropDefId = 0;
        AllowSowing = true;
        AllowHarvest = true;
    }
}
