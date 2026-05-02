using System;
using CowColonySim.Sim;
using CowColonySim.Sim.Terrain;
using Godot;

namespace CowColonySim.Game.Terrain;

// Mouse → world-space terrain hit. The old Y=0 plane projection drifted
// badly on hills (clicking the top of a slope landed in Y=0 well past
// the visible peak). This walks the camera ray in coarse steps, finds
// the first segment where the ray drops below the heightfield surface,
// then bisects to refine.
//
// elevatedTopMetres (optional): per-tile additional surface height in
// metres that overrides the heightfield where higher (wall tops, roofs).
// Used by RMB pathing so clicks on a roof land on the roof, not the
// ground beneath.
public static class TerrainRayCast
{
    private const int BisectIterations = 14;
    private const float MaxRayUnits = 5000f;
    private const float CoarseStepFraction = 0.4f;

    public static Vector3? Project(
        Camera3D camera,
        Vector2 mousePos,
        Heightfield field,
        Func<int, int, float>? elevatedTopMetres = null)
    {
        var origin = camera.ProjectRayOrigin(mousePos);
        var dir = camera.ProjectRayNormal(mousePos).Normalized();
        var unitsPerMeter = SimConstants.GodotUnitsPerTile / SimConstants.MetersPerTile;
        var stepUnits = SimConstants.GodotUnitsPerTile * CoarseStepFraction;

        var prevT = 0f;
        var prevAbove = origin.Y > Surface(field, origin, unitsPerMeter, elevatedTopMetres);

        for (var t = stepUnits; t < MaxRayUnits; t += stepUnits)
        {
            var p = origin + dir * t;
            var above = p.Y > Surface(field, p, unitsPerMeter, elevatedTopMetres);
            if (prevAbove && !above)
            {
                var lo = prevT;
                var hi = t;
                for (var i = 0; i < BisectIterations; i++)
                {
                    var mid = (lo + hi) * 0.5f;
                    var pm = origin + dir * mid;
                    if (pm.Y > Surface(field, pm, unitsPerMeter, elevatedTopMetres)) lo = mid;
                    else hi = mid;
                }
                return origin + dir * ((lo + hi) * 0.5f);
            }
            prevT = t;
            prevAbove = above;
        }
        return null;
    }

    // Inflate wall/roof tops outward into neighbouring tiles by this fraction
    // so clicking near (but not perfectly on) a wall top still snaps to the
    // wall. Without this, the ray skims past the wall top and lands on the
    // ground tile behind it — wall-top targeting becomes pixel-precise misery.
    // 0.35 of a tile ≈ 0.5m of forgiveness on each edge.
    private const float ElevatedInflate = 0.35f;

    private static float Surface(
        Heightfield field, Vector3 p, float unitsPerMeter, Func<int, int, float>? elevatedTopMetres)
    {
        var tilesX = p.X / SimConstants.GodotUnitsPerTile;
        var tilesY = p.Z / SimConstants.GodotUnitsPerTile;
        var groundUnits = field.SurfaceMetresAt(tilesX, tilesY) * unitsPerMeter;
        if (elevatedTopMetres is null) return groundUnits;
        var tx = (int)MathF.Floor(tilesX);
        var ty = (int)MathF.Floor(tilesY);
        var elevatedUnits = elevatedTopMetres(tx, ty) * unitsPerMeter;
        var fracX = tilesX - tx;
        var fracY = tilesY - ty;
        if (fracX < ElevatedInflate)
        {
            var n = elevatedTopMetres(tx - 1, ty) * unitsPerMeter;
            if (n > elevatedUnits) elevatedUnits = n;
        }
        if (fracX > 1f - ElevatedInflate)
        {
            var n = elevatedTopMetres(tx + 1, ty) * unitsPerMeter;
            if (n > elevatedUnits) elevatedUnits = n;
        }
        if (fracY < ElevatedInflate)
        {
            var n = elevatedTopMetres(tx, ty - 1) * unitsPerMeter;
            if (n > elevatedUnits) elevatedUnits = n;
        }
        if (fracY > 1f - ElevatedInflate)
        {
            var n = elevatedTopMetres(tx, ty + 1) * unitsPerMeter;
            if (n > elevatedUnits) elevatedUnits = n;
        }
        return elevatedUnits > groundUnits ? elevatedUnits : groundUnits;
    }
}
