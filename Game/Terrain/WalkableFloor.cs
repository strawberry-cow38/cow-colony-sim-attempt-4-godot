using CowColonySim.Sim;
using CowColonySim.Sim.Terrain;

namespace CowColonySim.Game.Terrain;

// Resolves the world-Y of the surface a colonist (or path tile) is standing
// on. The naive `max(terrainGround, simZ)` rule keeps wall-top + ladder-climb
// visuals correct but leaves colonists hovering over uneven terrain when
// their TileZ snaps to a layer that overshoots the actual ground (e.g.
// terrain at 0.95m but FloorLayer rounds to 1 → 1.5m): renderer would clamp
// feet to 1.5m and the colonist visibly floats. Threshold check fixes that:
// if sim-Z is within one height quantum of the terrain ground, treat it as
// ground-floor and hug the heightfield. Otherwise the entity is on an
// elevated layer (wall top, mid-climb, structure deck) — use the sim-Z
// directly so they ride the structure surface.
public static class WalkableFloor
{
    private const float QuantumThresholdMeters = 0.75f;

    public static float FeetUnits(Heightfield field, float unitsPerMeter, float metersX, float metersY, float simMetersZ)
    {
        var tilesX = metersX / SimConstants.MetersPerTile;
        var tilesY = metersY / SimConstants.MetersPerTile;
        var groundUnits = field.SurfaceMetresAt(tilesX, tilesY) * unitsPerMeter;
        var simUnits = simMetersZ * unitsPerMeter;
        var thresholdUnits = QuantumThresholdMeters * unitsPerMeter;
        if (MathF.Abs(simUnits - groundUnits) <= thresholdUnits) return groundUnits;
        return simUnits;
    }
}
