namespace CowColonySim.Sim.Items;

// Crafted-quality tier for clothing instances. Stored byte-backed on the
// stack so it serializes compact. Affects sell value, insulation, and
// effective max durability via per-quality multipliers.
public enum ClothingQuality : byte
{
    Awful = 0,
    Poor = 1,
    Normal = 2,
    Good = 3,
    Excellent = 4,
    Masterwork = 5,
    Legendary = 6,
}

public sealed class ClothingQualityDef
{
    public required ClothingQuality Quality { get; init; }
    public required string DisplayName { get; init; }
    public float DurabilityMultiplier { get; init; } = 1f;
    public float InsulationMultiplier { get; init; } = 1f;
    public float SellValueMultiplier { get; init; } = 1f;
}

public static class ClothingQualities
{
    private static readonly Dictionary<ClothingQuality, ClothingQualityDef> _byId = new();

    static ClothingQualities()
    {
        Register(new() { Quality = ClothingQuality.Awful,      DisplayName = "Awful",      DurabilityMultiplier = 0.5f,  InsulationMultiplier = 0.7f,  SellValueMultiplier = 0.4f });
        Register(new() { Quality = ClothingQuality.Poor,       DisplayName = "Poor",       DurabilityMultiplier = 0.75f, InsulationMultiplier = 0.85f, SellValueMultiplier = 0.7f });
        Register(new() { Quality = ClothingQuality.Normal,     DisplayName = "Normal",     DurabilityMultiplier = 1.0f,  InsulationMultiplier = 1.0f,  SellValueMultiplier = 1.0f });
        Register(new() { Quality = ClothingQuality.Good,       DisplayName = "Good",       DurabilityMultiplier = 1.25f, InsulationMultiplier = 1.15f, SellValueMultiplier = 1.5f });
        Register(new() { Quality = ClothingQuality.Excellent,  DisplayName = "Excellent",  DurabilityMultiplier = 1.5f,  InsulationMultiplier = 1.3f,  SellValueMultiplier = 2.5f });
        Register(new() { Quality = ClothingQuality.Masterwork, DisplayName = "Masterwork", DurabilityMultiplier = 2.0f,  InsulationMultiplier = 1.5f,  SellValueMultiplier = 5.0f });
        Register(new() { Quality = ClothingQuality.Legendary,  DisplayName = "Legendary",  DurabilityMultiplier = 3.0f,  InsulationMultiplier = 1.75f, SellValueMultiplier = 10.0f });
    }

    public static void Register(ClothingQualityDef def) => _byId[def.Quality] = def;

    public static ClothingQualityDef Get(ClothingQuality q) =>
        _byId.TryGetValue(q, out var d) ? d : _byId[ClothingQuality.Normal];

    public static string DisplayName(ClothingQuality q) =>
        _byId.TryGetValue(q, out var d) ? d.DisplayName : string.Empty;
}
