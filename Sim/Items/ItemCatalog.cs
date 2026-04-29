namespace CowColonySim.Sim.Items;

// Static registry of item defs keyed by string Id. Mirrors BlueprintCatalog
// — boot once via Register, lookups are reads. Existing ItemKind enum entries
// each have a default def registered here so legacy spawn paths (chop yields,
// AddOrMergeItem) keep working without naming a defId.
public static class ItemCatalog
{
    private static readonly Dictionary<string, ItemDef> _byId = new(StringComparer.Ordinal);
    private static readonly Dictionary<ItemKind, string> _kindDefault = new();

    static ItemCatalog()
    {
        Register(new ItemDef
        {
            Id = "wood", Kind = ItemKind.Wood,
            DisplayName = "Wood log", Description = "Felled tree segment. Splits into planks.",
            Weight = 1f, Bulk = 0.4f, SellValue = 2,
            Stackable = true, StackCapacity = 75,
        });
        Register(new ItemDef
        {
            Id = "stone", Kind = ItemKind.Stone,
            DisplayName = "Stone chunk", Description = "Mined boulder fragment. Walls, hearths.",
            Weight = 3f, Bulk = 1.5f, SellValue = 3,
            Stackable = true, StackCapacity = 50,
        });
        Register(new ItemDef
        {
            Id = "wheat", Kind = ItemKind.Wheat,
            DisplayName = "Wheat", Description = "Harvested grain. Mills to flour.",
            Weight = 0.4f, Bulk = 0.3f, SellValue = 1,
            Stackable = true, StackCapacity = 75,
        });
        // Minified items wrap a structure — true weight comes from the
        // wrapped def, but we don't bake that lookup here. Conservative
        // default; per-instance overrides can layer on later.
        Register(new ItemDef
        {
            Id = "minified", Kind = ItemKind.Minified,
            DisplayName = "Minified thing", Description = "A packaged structure ready to reinstall.",
            Weight = 25f, Bulk = 20f, SellValue = 0,
            Stackable = false, StackCapacity = 1,
        });

        // Phase-1 demo gear so tests + UI have something real to chew on.
        // Force-equipping a backpack is how we exercise EquippedBulkBonus.
        Register(new ItemDef
        {
            Id = "apparel.backpack", Kind = ItemKind.Apparel,
            DisplayName = "Backpack", Description = "Doubles your carrying bulk.",
            Weight = 1.5f, Bulk = 1f, SellValue = 30,
            Stackable = false, StackCapacity = 1,
            IsClothing = true, ClothingLayer = ClothingLayer.OnBack,
            EquippedBulkBonus = 30f,
            BaseDurability = 120f, InsulationCold = 0f, InsulationHeat = 0f,
        });
        Register(new ItemDef
        {
            Id = "apparel.shirt", Kind = ItemKind.Apparel,
            DisplayName = "Shirt", Description = "Basic torso layer.",
            Weight = 0.4f, Bulk = 0.3f, SellValue = 8,
            Stackable = false, StackCapacity = 1,
            IsClothing = true, ClothingLayer = ClothingLayer.TorsoMid,
            BaseDurability = 100f, InsulationCold = 8f, InsulationHeat = 2f,
        });
        Register(new ItemDef
        {
            Id = "apparel.pants", Kind = ItemKind.Apparel,
            DisplayName = "Pants", Description = "Basic leg layer.",
            Weight = 0.5f, Bulk = 0.35f, SellValue = 10,
            Stackable = false, StackCapacity = 1,
            IsClothing = true, ClothingLayer = ClothingLayer.Legs,
            BaseDurability = 100f, InsulationCold = 10f, InsulationHeat = 2f,
        });
        Register(new ItemDef
        {
            Id = "weapon.club", Kind = ItemKind.Weapon,
            DisplayName = "Wooden club", Description = "A heavy stick. Better than fists.",
            Weight = 2f, Bulk = 1.5f, SellValue = 12,
            Stackable = false, StackCapacity = 1,
            IsWeapon = true,
        });
    }

    public static void Register(ItemDef def)
    {
        _byId[def.Id] = def;
        // First registration of a given kind becomes its default lookup.
        // Re-registration replaces the entry but never silently swaps the
        // legacy default — explicit RegisterKindDefault for that.
        if (!_kindDefault.ContainsKey(def.Kind)) _kindDefault[def.Kind] = def.Id;
    }

    public static ItemDef Get(string id) => _byId[id];
    public static bool TryGet(string id, out ItemDef? def) => _byId.TryGetValue(id, out def);

    public static string DefaultIdFor(ItemKind kind) => _kindDefault[kind];
    public static ItemDef DefaultFor(ItemKind kind) => _byId[_kindDefault[kind]];
}
