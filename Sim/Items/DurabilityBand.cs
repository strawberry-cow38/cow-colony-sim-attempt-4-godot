namespace CowColonySim.Sim.Items;

// Coarse condition bands for clothing durability percentage. Pristine 85+,
// Worn 50-85, Damaged 25-50, Tattered 10-25, Ruined <10 but >0, Destroyed
// at 0. Ruined items can't be repaired and salvage to nothing.
public enum DurabilityBand : byte
{
    Destroyed = 0,
    Ruined = 1,
    Tattered = 2,
    Damaged = 3,
    Worn = 4,
    Pristine = 5,
}

public static class DurabilityBands
{
    public static DurabilityBand BandFor(float pct)
    {
        if (pct <= 0f) return DurabilityBand.Destroyed;
        if (pct < 10f) return DurabilityBand.Ruined;
        if (pct < 25f) return DurabilityBand.Tattered;
        if (pct < 50f) return DurabilityBand.Damaged;
        if (pct < 85f) return DurabilityBand.Worn;
        return DurabilityBand.Pristine;
    }

    public static string DisplayName(DurabilityBand b) => b switch
    {
        DurabilityBand.Pristine => "Pristine",
        DurabilityBand.Worn => "Worn",
        DurabilityBand.Damaged => "Damaged",
        DurabilityBand.Tattered => "Tattered",
        DurabilityBand.Ruined => "Ruined",
        DurabilityBand.Destroyed => "Destroyed",
        _ => string.Empty,
    };

    public static bool CanRepair(DurabilityBand b) =>
        b is DurabilityBand.Worn or DurabilityBand.Damaged or DurabilityBand.Tattered;
}
