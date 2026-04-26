using CowColonySim.Sim;
using CowColonySim.Sim.Terrain;
using Godot;

namespace CowColonySim.Game.Terrain;

// Mouse → world-space terrain hit. The old Y=0 plane projection drifted
// badly on hills (clicking the top of a slope landed in Y=0 well past
// the visible peak). This walks the camera ray in coarse steps, finds
// the first segment where the ray drops below the heightfield surface,
// then bisects to refine.
public static class TerrainRayCast
{
    private const int BisectIterations = 14;
    private const float MaxRayUnits = 5000f;
    private const float CoarseStepFraction = 0.4f;

    public static Vector3? Project(Camera3D camera, Vector2 mousePos, Heightfield field)
    {
        var origin = camera.ProjectRayOrigin(mousePos);
        var dir = camera.ProjectRayNormal(mousePos).Normalized();
        var unitsPerMeter = SimConstants.GodotUnitsPerTile / SimConstants.MetersPerTile;
        var stepUnits = SimConstants.GodotUnitsPerTile * CoarseStepFraction;

        var prevT = 0f;
        var prevAbove = origin.Y > Surface(field, origin, unitsPerMeter);

        for (var t = stepUnits; t < MaxRayUnits; t += stepUnits)
        {
            var p = origin + dir * t;
            var above = p.Y > Surface(field, p, unitsPerMeter);
            if (prevAbove && !above)
            {
                var lo = prevT;
                var hi = t;
                for (var i = 0; i < BisectIterations; i++)
                {
                    var mid = (lo + hi) * 0.5f;
                    var pm = origin + dir * mid;
                    if (pm.Y > Surface(field, pm, unitsPerMeter)) lo = mid;
                    else hi = mid;
                }
                return origin + dir * ((lo + hi) * 0.5f);
            }
            prevT = t;
            prevAbove = above;
        }
        return null;
    }

    private static float Surface(Heightfield field, Vector3 p, float unitsPerMeter)
    {
        var tilesX = p.X / SimConstants.GodotUnitsPerTile;
        var tilesY = p.Z / SimConstants.GodotUnitsPerTile;
        return field.SurfaceMetresAt(tilesX, tilesY) * unitsPerMeter;
    }
}
