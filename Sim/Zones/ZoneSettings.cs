namespace CowColonySim.Sim.Zones;

// Per-ZoneType settings stub. Real fields land when each zone gets
// fleshed out (filter list for stockpiles, crop+growth for farms).
// Kept as separate components so each zone entity only carries the
// settings struct that matches its ZoneType.

public struct StockpileSettings
{
    public int Priority;
}

public struct FarmSettings
{
    public int CropDefId;
}
