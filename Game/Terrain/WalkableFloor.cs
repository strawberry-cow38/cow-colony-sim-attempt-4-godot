using CowColonySim.Sim;
using CowColonySim.Sim.Terrain;

namespace CowColonySim.Game.Terrain;

// Resolves the world-Y of the surface a colonist (or path tile) is standing
// on. Three regimes:
//
//   - Walking on terrain: sim TileZ snaps to the *source* tile's FloorLayer
//     and lags behind during a horizontal step across a slope. We can't trust
//     sim-Z here — must use the interpolated heightfield surface so the
//     colonist hugs the terrain smoothly.
//   - Standing on / walking across a built walkable top (wall, roof,
//     ladder summit): WalkableTopLookup tells us the top metres for that
//     tile. When sim-Z is near that top, snap to it.
//   - Mid-ladder climb (between layers): sim-Z is the source of truth and
//     genuinely above ground; nothing else moves the colonist vertically
//     during the climb.
public static class WalkableFloor
{
    private const float TopMatchToleranceMetres = 0.25f;
    // Half a tile of vertical clearance — bigger than any single-tile slope
    // step (0.75m max) so a colonist whose sim TileZ lags during a horizontal
    // walk across a slope still reads as "on terrain" and hugs the ground.
    // Mid-ladder climbs sit well above this and read as "trust sim".
    private const float MidClimbThresholdMetres = 0.75f;

    public static float FeetUnits(
        Heightfield field, float unitsPerMeter,
        float metersX, float metersY, float simMetersZ,
        Func<int, int, float>? elevatedTopMetres = null)
    {
        var tilesX = metersX / SimConstants.MetersPerTile;
        var tilesY = metersY / SimConstants.MetersPerTile;
        var groundMetres = field.SurfaceMetresAt(tilesX, tilesY);

        if (elevatedTopMetres is not null)
        {
            var tx = (int)MathF.Floor(tilesX);
            var ty = (int)MathF.Floor(tilesY);
            var top = elevatedTopMetres(tx, ty);
            if (top > 0f && MathF.Abs(simMetersZ - top) <= TopMatchToleranceMetres)
            {
                return top * unitsPerMeter;
            }
        }

        if (simMetersZ > groundMetres + MidClimbThresholdMetres)
        {
            return simMetersZ * unitsPerMeter;
        }

        return groundMetres * unitsPerMeter;
    }
}
