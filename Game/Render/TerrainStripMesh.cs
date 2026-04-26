using CowColonySim.Sim;
using CowColonySim.Sim.Terrain;
using Godot;

namespace CowColonySim.Game.Render;

// Builds a flat-shaded triangle strip that hugs the heightfield over a
// tile rect — two triangles per tile snapped to the four corner heights,
// matching TerrainMeshBuilder's TR→BL diagonal so the strip lies on
// the same plane as the terrain on slopes. Optional per-tile mask skips
// tiles that aren't part of the zone (post-merge L-shapes etc).
internal static class TerrainStripMesh
{
    public static ArrayMesh Build(
        Heightfield field, float unitsPerMeter,
        int minTileX, int minTileY, int maxTileX, int maxTileY,
        float hoverUnits,
        bool[]? mask = null)
    {
        var widthTiles = maxTileX - minTileX + 1;
        var heightTiles = maxTileY - minTileY + 1;
        var unitsPerTile = SimConstants.GodotUnitsPerTile;

        var verts = new List<Vector3>(widthTiles * heightTiles * 6);

        for (var ty = 0; ty < heightTiles; ty++)
        {
            for (var tx = 0; tx < widthTiles; tx++)
            {
                if (mask is not null && !mask[ty * widthTiles + tx]) continue;

                var gx = minTileX + tx;
                var gy = minTileY + ty;

                var x0 = gx * unitsPerTile;
                var x1 = (gx + 1) * unitsPerTile;
                var y0 = gy * unitsPerTile;
                var y1 = (gy + 1) * unitsPerTile;

                var h00 = Sample(field, gx, gy, unitsPerMeter, hoverUnits);
                var h10 = Sample(field, gx + 1, gy, unitsPerMeter, hoverUnits);
                var h01 = Sample(field, gx, gy + 1, unitsPerMeter, hoverUnits);
                var h11 = Sample(field, gx + 1, gy + 1, unitsPerMeter, hoverUnits);

                var p00 = new Vector3(x0, h00, y0);
                var p10 = new Vector3(x1, h10, y0);
                var p01 = new Vector3(x0, h01, y1);
                var p11 = new Vector3(x1, h11, y1);

                // TR→BL diagonal matches TerrainMeshBuilder so strip and
                // terrain share the same plane on non-coplanar quads.
                verts.Add(p00); verts.Add(p10); verts.Add(p01);
                verts.Add(p10); verts.Add(p11); verts.Add(p01);
            }
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts.ToArray();

        var mesh = new ArrayMesh();
        if (verts.Count > 0)
        {
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        }
        return mesh;
    }

    private static float Sample(Heightfield field, int vertX, int vertY, float unitsPerMeter, float hoverUnits) =>
        field.SurfaceMetresAt(vertX, vertY) * unitsPerMeter + hoverUnits;
}
