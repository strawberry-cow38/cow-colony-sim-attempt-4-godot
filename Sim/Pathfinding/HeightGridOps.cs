using CowColonySim.Sim.Blueprints;

namespace CowColonySim.Sim.Pathfinding;

// Centralised def-aware register/unregister helpers for HeightGrid. Kept
// out of HeightGrid itself so the grid stays free of BlueprintCatalog
// dependencies; call sites (construction completion, instant placement,
// uninstall, deconstruct) all funnel through here so the wall-top /
// ladder bookkeeping stays consistent.
public static class HeightGridOps
{
    public static void RegisterStructure(HeightGrid grid, BlueprintDef def, int x, int y, int baseLayer)
    {
        if (def.IsLadder)
        {
            // Ladders don't block ground — colonist stands on tile, then climbs.
            // Top layer registered as walkable so colonist can dismount onto an
            // adjacent wall/roof top (or just stand on the ladder summit).
            var topLayer = baseLayer + def.LadderSpanQuanta / 2;
            grid.AddLadder(x, y, baseLayer, topLayer);
            grid.AddWalkableLayer(x, y, topLayer);
            return;
        }
        grid.MarkBlocked(x, y, true);
        if (def.WalkableTop)
        {
            grid.AddWalkableLayer(x, y, baseLayer + def.HeightQuanta / 2);
        }
        if (def.BlocksLightAndRain)
        {
            grid.AddRoof(x, y);
        }
    }

    public static void UnregisterStructure(HeightGrid grid, BlueprintDef def, int x, int y, int baseLayer)
    {
        if (def.IsLadder)
        {
            var topLayer = baseLayer + def.LadderSpanQuanta / 2;
            grid.RemoveLadder(x, y, baseLayer, topLayer);
            grid.RemoveWalkableLayer(x, y, topLayer);
            return;
        }
        grid.MarkBlocked(x, y, false);
        if (def.WalkableTop)
        {
            grid.RemoveWalkableLayer(x, y, baseLayer + def.HeightQuanta / 2);
        }
        if (def.BlocksLightAndRain)
        {
            grid.RemoveRoof(x, y);
        }
    }
}
