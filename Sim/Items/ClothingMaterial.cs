namespace CowColonySim.Sim.Items;

// Material a clothing instance is made from. Stored on InventoryStack so
// two cotton shirts and one wool shirt share the same def but read
// different per-instance stats. Byte-backed for compact serialization.
public enum ClothingMaterial : byte
{
    None = 0,
    Cotton = 1,
}

public sealed class ClothingMaterialDef
{
    public required ClothingMaterial Material { get; init; }
    public required string DisplayName { get; init; }
    // Multipliers applied on top of the def's base stats.
    public float WeightMultiplier { get; init; } = 1f;
    public float BulkMultiplier { get; init; } = 1f;
    public float DurabilityMultiplier { get; init; } = 1f;
    public float InsulationColdMultiplier { get; init; } = 1f;
    public float InsulationHeatMultiplier { get; init; } = 1f;
    public float SellValueMultiplier { get; init; } = 1f;
}

public static class ClothingMaterials
{
    private static readonly Dictionary<ClothingMaterial, ClothingMaterialDef> _byId = new();

    static ClothingMaterials()
    {
        Register(new ClothingMaterialDef
        {
            Material = ClothingMaterial.Cotton,
            DisplayName = "Cotton",
            WeightMultiplier = 1f,
            BulkMultiplier = 1f,
            DurabilityMultiplier = 1f,
            InsulationColdMultiplier = 0.8f,
            InsulationHeatMultiplier = 0.5f,
            SellValueMultiplier = 1f,
        });
    }

    public static void Register(ClothingMaterialDef def) => _byId[def.Material] = def;

    public static ClothingMaterialDef Get(ClothingMaterial m) =>
        _byId.TryGetValue(m, out var d) ? d : _byId[ClothingMaterial.Cotton];

    public static string DisplayName(ClothingMaterial m) =>
        _byId.TryGetValue(m, out var d) ? d.DisplayName : string.Empty;
}
