using CowColonySim.Sim;
using CowColonySim.Sim.Terrain;
using Godot;

namespace CowColonySim.Game.Terrain;

// Per-tile mesh: 4 unshared corners per tile (so per-tile albedo / decals
// can stay discrete later if we want), but each corner carries a SMOOTH
// normal computed from heightfield central differences. Lighting
// interpolates smoothly across each tile and across tile borders — no
// "knife edge" facets on 1-low-3-high or 3-low-1-high configurations.
//
// (CLAUDE.md previously locked the faceted look. The user explicitly
// asked for smooth render-side lighting on 2026-04-26 — see git log.)
public static class TerrainMeshBuilder
{
    public static ArrayMesh Build(Heightfield field, float? unitsPerTileOverride = null)
    {
        var tilesX = field.VertWidth - 1;
        var tilesY = field.VertHeight - 1;
        var tileCount = tilesX * tilesY;
        var vertCount = tileCount * 4;
        var indexCount = tileCount * 6;

        var verts = new Vector3[vertCount];
        var normals = new Vector3[vertCount];
        var uvs = new Vector2[vertCount];
        var indices = new int[indexCount];

        var unitsPerTile = unitsPerTileOverride ?? SimConstants.GodotUnitsPerTile;
        var unitsPerQuanta = TerrainConstants.VerticalQuantumMetres
                           * (SimConstants.GodotUnitsPerTile / SimConstants.MetersPerTile);

        // Pre-compute smooth normal per (vx, vy) on the vertex grid.
        var smoothNormals = BuildSmoothNormals(field, unitsPerTile, unitsPerQuanta);

        var vi = 0;
        var ii = 0;
        for (var ty = 0; ty < tilesY; ty++)
        {
            for (var tx = 0; tx < tilesX; tx++)
            {
                var hTL = field.Get(tx, ty) * unitsPerQuanta;
                var hTR = field.Get(tx + 1, ty) * unitsPerQuanta;
                var hBL = field.Get(tx, ty + 1) * unitsPerQuanta;
                var hBR = field.Get(tx + 1, ty + 1) * unitsPerQuanta;

                var x0 = tx * unitsPerTile;
                var x1 = (tx + 1) * unitsPerTile;
                var z0 = ty * unitsPerTile;
                var z1 = (ty + 1) * unitsPerTile;

                var iTL = vi;
                var iTR = vi + 1;
                var iBL = vi + 2;
                var iBR = vi + 3;

                verts[iTL] = new Vector3(x0, hTL, z0);
                verts[iTR] = new Vector3(x1, hTR, z0);
                verts[iBL] = new Vector3(x0, hBL, z1);
                verts[iBR] = new Vector3(x1, hBR, z1);

                normals[iTL] = smoothNormals[(ty)     * field.VertWidth + tx];
                normals[iTR] = smoothNormals[(ty)     * field.VertWidth + tx + 1];
                normals[iBL] = smoothNormals[(ty + 1) * field.VertWidth + tx];
                normals[iBR] = smoothNormals[(ty + 1) * field.VertWidth + tx + 1];

                uvs[iTL] = new Vector2(0f, 0f);
                uvs[iTR] = new Vector2(1f, 0f);
                uvs[iBL] = new Vector2(0f, 1f);
                uvs[iBR] = new Vector2(1f, 1f);

                indices[ii++] = iTL;
                indices[ii++] = iBL;
                indices[ii++] = iTR;
                indices[ii++] = iTR;
                indices[ii++] = iBL;
                indices[ii++] = iBR;

                vi += 4;
            }
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Index] = indices;

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    // Central-difference normal at every vertex of the heightfield grid.
    // Edge verts clamp the neighbor lookup to in-bounds (forward/backward
    // difference falls out naturally — the divisor stays 2*unitsPerTile so
    // edges look slightly flatter, which is fine).
    private static Vector3[] BuildSmoothNormals(Heightfield field, float unitsPerTile, float unitsPerQuanta)
    {
        var w = field.VertWidth;
        var h = field.VertHeight;
        var result = new Vector3[w * h];
        for (var y = 0; y < h; y++)
        {
            var ym = y > 0 ? y - 1 : y;
            var yp = y < h - 1 ? y + 1 : y;
            for (var x = 0; x < w; x++)
            {
                var xm = x > 0 ? x - 1 : x;
                var xp = x < w - 1 ? x + 1 : x;
                var hL = field.Get(xm, y) * unitsPerQuanta;
                var hR = field.Get(xp, y) * unitsPerQuanta;
                var hD = field.Get(x, ym) * unitsPerQuanta;
                var hU = field.Get(x, yp) * unitsPerQuanta;
                var dHx = (hR - hL) / (2f * unitsPerTile);
                var dHz = (hU - hD) / (2f * unitsPerTile);
                result[y * w + x] = new Vector3(-dHx, 1f, -dHz).Normalized();
            }
        }
        return result;
    }
}
