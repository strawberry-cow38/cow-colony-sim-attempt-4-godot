using CowColonySim.Sim;
using CowColonySim.Sim.Terrain;
using Godot;

namespace CowColonySim.Game.Terrain;

// Builds an ArrayMesh from a sim Heightfield. Vertices snap to the tile-corner
// lattice horizontally and to 0.75m steps vertically. Normals computed from
// per-vertex height neighbours. One surface, no chunking yet.
public static class TerrainMeshBuilder
{
    public static ArrayMesh Build(Heightfield field)
    {
        var w = field.VertWidth;
        var h = field.VertHeight;
        var vertCount = w * h;
        var quadCount = (w - 1) * (h - 1);
        var indexCount = quadCount * 6;

        var verts = new Vector3[vertCount];
        var normals = new Vector3[vertCount];
        var indices = new int[indexCount];

        var unitsPerTile = SimConstants.GodotUnitsPerTile;
        var unitsPerQuanta = TerrainConstants.VerticalQuantumMetres
                           * (SimConstants.GodotUnitsPerTile / SimConstants.MetersPerTile);

        for (var vy = 0; vy < h; vy++)
        {
            for (var vx = 0; vx < w; vx++)
            {
                var idx = vy * w + vx;
                var heightUnits = field.Get(vx, vy) * unitsPerQuanta;
                verts[idx] = new Vector3(vx * unitsPerTile, heightUnits, vy * unitsPerTile);
            }
        }

        for (var vy = 0; vy < h; vy++)
        {
            for (var vx = 0; vx < w; vx++)
            {
                var hl = field.Get(Math.Max(vx - 1, 0), vy);
                var hr = field.Get(Math.Min(vx + 1, w - 1), vy);
                var hd = field.Get(vx, Math.Max(vy - 1, 0));
                var hu = field.Get(vx, Math.Min(vy + 1, h - 1));
                var dx = (hr - hl) * unitsPerQuanta;
                var dz = (hu - hd) * unitsPerQuanta;
                var spanX = (vx == 0 || vx == w - 1) ? unitsPerTile : 2f * unitsPerTile;
                var spanZ = (vy == 0 || vy == h - 1) ? unitsPerTile : 2f * unitsPerTile;
                var n = new Vector3(-dx / spanX, 1f, -dz / spanZ).Normalized();
                normals[vy * w + vx] = n;
            }
        }

        var i = 0;
        for (var vy = 0; vy < h - 1; vy++)
        {
            for (var vx = 0; vx < w - 1; vx++)
            {
                var i00 = vy * w + vx;
                var i10 = i00 + 1;
                var i01 = i00 + w;
                var i11 = i01 + 1;
                indices[i++] = i00;
                indices[i++] = i01;
                indices[i++] = i10;
                indices[i++] = i10;
                indices[i++] = i01;
                indices[i++] = i11;
            }
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Index] = indices;

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }
}
