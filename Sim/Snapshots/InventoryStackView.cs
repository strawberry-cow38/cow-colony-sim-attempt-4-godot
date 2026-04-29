namespace CowColonySim.Sim.Snapshots;

// Immutable per-frame view of one inventory entry. Renderer uses this
// to draw inventory rows + force-drop / equip buttons. Display fields
// (DisplayName, Description, SellValue) come from the ItemDef so the
// panel doesn't need to read the catalog itself. Material/Quality/Durability
// are sentinels for non-clothing stacks (None / Normal / -1).
public readonly record struct InventoryStackView(
    int Index,
    string DefId,
    string DisplayName,
    string Description,
    int Count,
    float Weight,
    float Bulk,
    int SellValue,
    bool Equipped,
    bool Locked,
    bool IsWeapon,
    bool IsClothing,
    string WrappedDefId = "",
    Items.ClothingMaterial Material = Items.ClothingMaterial.None,
    Items.ClothingQuality Quality = Items.ClothingQuality.Normal,
    float Durability = -1f);
