namespace CowColonySim.Sim.Items;

// Static registry entry describing an item. Stackables (wood, wheat) and
// uniques (weapon, clothing) share this shape — Stackable + StackCapacity
// gate the stacking behavior. Weight (kg) and Bulk (L) feed colonist
// inventory caps. SellValue is silver per unit.
public sealed class ItemDef
{
    public required string Id { get; init; }
    public required ItemKind Kind { get; init; }
    public required string DisplayName { get; init; }
    public string Description { get; init; } = string.Empty;
    public float Weight { get; init; }
    public float Bulk { get; init; }
    public int SellValue { get; init; }
    public bool Stackable { get; init; } = true;
    public int StackCapacity { get; init; } = 50;

    public bool IsWeapon { get; init; }
    public bool IsClothing { get; init; }
    public ClothingLayer ClothingLayer { get; init; } = ClothingLayer.None;

    // Bonus carry-bulk granted while equipped (backpacks, vests). Applied
    // through CarryCaps.MaxBulkOf when this def's stack has Equipped=true.
    public float EquippedBulkBonus { get; init; }
    // Bonus carry-weight (power armor / exo). Phase-3 gear hooks here.
    public float EquippedWeightBonus { get; init; }

    // Clothing-only stats. BaseDurability is the def-baseline pre-multiplier
    // ceiling (HP, not %). Insulation values are unitless contribution to
    // colonist warmth/cool — material + quality scale on top.
    public float BaseDurability { get; init; }
    public float InsulationCold { get; init; }
    public float InsulationHeat { get; init; }
}
