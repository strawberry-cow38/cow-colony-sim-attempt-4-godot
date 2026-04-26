namespace CowColonySim.Sim.Plants;

// Stable ids for the plant catalog. Trees use Tree; future crops slot
// in as new entries here so growth rates / yields can be looked up by
// id without dragging a string label through every component.
public static class CropDefIds
{
    public const int Tree = 0;
    public const int Wheat = 1;
}

// Per-crop growth tuning: how fast a plant climbs to 100% growth under
// ideal conditions, and how long it lingers fully mature before
// withering. GrowthPerTickAtFullSun is in growth-percent per tick;
// PlantGrowthSystem multiplies by current sunlight (0..1) when sunlight
// ≥ MinSunlightFraction. Below that the plant idles.
public readonly record struct CropDef(
    int Id,
    string Label,
    bool IsTree,
    float GrowthPerTickAtFullSun,
    float MinSunlightFraction,
    int LifespanTicks);

public static class CropCatalog
{
    private static readonly Dictionary<int, CropDef> _byId = Build();

    public static CropDef Get(int id) => _byId.TryGetValue(id, out var d) ? d : _byId[CropDefIds.Tree];

    private static Dictionary<int, CropDef> Build()
    {
        // Numbers are deliberately fast for pre-alpha visibility — full
        // mature in well under one in-game day so we can watch the loop.
        var d = new Dictionary<int, CropDef>
        {
            [CropDefIds.Tree] = new(CropDefIds.Tree, "Tree", IsTree: true,
                GrowthPerTickAtFullSun: 0.05f,
                MinSunlightFraction: 0.51f,
                LifespanTicks: 60 * 60 * 30), // ~30 min real-time fully grown before wither
            [CropDefIds.Wheat] = new(CropDefIds.Wheat, "Wheat", IsTree: false,
                GrowthPerTickAtFullSun: 0.20f,
                MinSunlightFraction: 0.51f,
                LifespanTicks: 60 * 60 * 5),
        };
        return d;
    }
}
