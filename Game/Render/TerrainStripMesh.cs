using CowColonySim.Sim;
using CowColonySim.Sim.Terrain;
using Godot;

namespace CowColonySim.Game.Render;

// Builds a flat-shaded triangle strip that hugs the heightfield over a
// tile rect — two triangles per tile snapped to the four corner heights.
// Shared by ZonesRenderer (stockpiles + farms) and RectDragOverlay (live
// placement preview) so both follow slopes consistently.
internal static class TerrainStripMesh
{
    public static ArrayMesh Build(
        Heightfield field, float unitsPerMeter,
        int minTileX, int minTileY, int maxTileX, int maxTileY,
        float hoverUnits)
    {
        var widthTiles = maxTileX - minTileX + 1;
        var heightTiles = maxTileY - minTileY + 1;
        var verts = new Vector3[widthTiles * heightTiles * 6];
        var unitsPerTile = SimConstants.GodotUnitsPerTile;
        var v = 0;

        for (var ty = 0; ty < heightTiles; ty++)
        {
            for (var tx = 0; tx < widthTiles; tx++)
            {
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

                // Match TerrainMeshBuilder diagonal (TR→BL) so the strip lies
                // exactly on the terrain plane on non-coplanar quads — the
                // opposite diagonal would dip below at hill corners and the
                // hover offset wouldn't be enough to clear it.
                verts[v++] = p00; verts[v++] = p10; verts[v++] = p01;
                verts[v++] = p10; verts[v++] = p11; verts[v++] = p01;
            }
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    private static float Sample(Heightfield field, int vertX, int vertY, float unitsPerMeter, float hoverUnits) =>
        field.SurfaceMetresAt(vertX, vertY) * unitsPerMeter + hoverUnits;
}
