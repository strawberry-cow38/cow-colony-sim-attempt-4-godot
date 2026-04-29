using CowColonySim.Sim.Items;
using Friflo.Engine.ECS;

namespace CowColonySim.Sim.Zones;

// Per-ZoneType settings stub. Real fields land when each zone gets
// fleshed out (filter list for stockpiles, crop+growth for farms).
// Kept as separate components so each zone entity only carries the
// settings struct that matches its ZoneType.

public struct StockpileSettings : IComponent
{
    public int Priority;
    // Bitmask of ItemKind ordinals this stockpile accepts. Bit i = kind i.
    // Hauls only target stockpiles where the bit for the carried item's
    // kind is set; items already sitting in a stockpile that no longer
    // accepts their kind get re-hauled out next tick.
    public ulong AllowedKindsMask;

    public StockpileSettings()
    {
        Priority = 0;
        AllowedKindsMask = StockpileFilter.DefaultMask;
    }

    public bool Accepts(ItemKind kind) => StockpileFilter.MaskAccepts(AllowedKindsMask, kind);
}

// Helpers for the AllowedKindsMask. Centralized so save/load, command
// apply, and the haul system all agree on what "all kinds" means.
public static class StockpileFilter
{
    // Default = every real ItemKind allowed. None=0 stays cleared so an
    // unset/zero ulong still reads as "nothing accepted" which is a
    // useful invariant for save migrations.
    public static readonly ulong DefaultMask = BuildDefaultMask();

    public static bool MaskAccepts(ulong mask, ItemKind kind)
    {
        if (kind == ItemKind.None) return false;
        return (mask & (1UL << (int)kind)) != 0UL;
    }

    public static ulong WithKind(ulong mask, ItemKind kind, bool allow)
    {
        if (kind == ItemKind.None) return mask;
        var bit = 1UL << (int)kind;
        return allow ? (mask | bit) : (mask & ~bit);
    }

    private static ulong BuildDefaultMask()
    {
        ulong m = 0;
        foreach (ItemKind k in System.Enum.GetValues(typeof(ItemKind)))
        {
            if (k == ItemKind.None) continue;
            m |= 1UL << (int)k;
        }
        return m;
    }
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
