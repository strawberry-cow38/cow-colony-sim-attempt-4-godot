using CowColonySim.Sim;
using CowColonySim.Sim.Terrain;

namespace CowColonySim.Game.Terrain;

// Resolves the world-Y of the surface a colonist (or path tile) is standing
// on. Three regimes:
//
//   - On a ladder tile: trust sim-Z so mid-climb rides between layers.
//   - Standing on a built walkable top (wall, roof) within a tolerance of
//     the elevated surface: snap to the lookup's top metres.
//   - Anything else: hug the heightfield. Sim TileZ lags the source layer
//     during a horizontal step across a slope, so trusting sim-Z would
//     leave the colonist floating above the dest.
public static class WalkableFloor
{
    private const float TopMatchToleranceMetres = 0.5f;

    public static float FeetUnits(
        Heightfield field, float unitsPerMeter,
        float metersX, float metersY, float simMetersZ,
        Func<int, int, float>? elevatedTopMetres = null,
        Func<int, int, bool>? isLadderTile = null)
    {
        var tilesX = metersX / SimConstants.MetersPerTile;
        var tilesY = metersY / SimConstants.MetersPerTile;
        var tx = (int)MathF.Floor(tilesX);
        var ty = (int)MathF.Floor(tilesY);

        if (isLadderTile is not null && isLadderTile(tx, ty))
        {
            return simMetersZ * unitsPerMeter;
        }

        if (elevatedTopMetres is not null)
        {
            var top = elevatedTopMetres(tx, ty);
            if (top > 0f && MathF.Abs(simMetersZ - top) <= TopMatchToleranceMetres)
            {
                return top * unitsPerMeter;
            }
        }

        return field.SurfaceMetresAt(tilesX, tilesY) * unitsPerMeter;
    }
}
