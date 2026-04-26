using CowColonySim.Sim;
using CowColonySim.Sim.Terrain;
using Godot;

namespace CowColonySim.Game.Terrain;

// Per-tile mesh: 4 unshared corners per tile, ONE flat normal per tile
// (the average of the two triangle normals). This is the faceted look
// CLAUDE.md locks — crisp per-tile shading, no smooth interpolation
// across tile borders. Smooth normals were tried 2026-04-26 and rebuked
// for looking soft at distance; reverted to flat per-tile.
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

                var pTL = new Vector3(x0, hTL, z0);
                var pTR = new Vector3(x1, hTR, z0);
                var pBL = new Vector3(x0, hBL, z1);
                var pBR = new Vector3(x1, hBR, z1);

                verts[iTL] = pTL;
                verts[iTR] = pTR;
                verts[iBL] = pBL;
                verts[iBR] = pBR;

                // One flat normal per tile = average of the two triangle
                // normals (TL-TR-BL and TR-BR-BL, CCW from above).
                var n1 = (pTR - pTL).Cross(pBL - pTL);
                var n2 = (pBR - pTR).Cross(pBL - pTR);
                var nFlat = (n1 + n2).Normalized();

                normals[iTL] = nFlat;
                normals[iTR] = nFlat;
                normals[iBL] = nFlat;
                normals[iBR] = nFlat;

                uvs[iTL] = new Vector2(0f, 0f);
                uvs[iTR] = new Vector2(1f, 0f);
                uvs[iBL] = new Vector2(0f, 1f);
                uvs[iBR] = new Vector2(1f, 1f);

                indices[ii++] = iTL;
                indices[ii++] = iTR;
                indices[ii++] = iBL;
                indices[ii++] = iTR;
                indices[ii++] = iBR;
                indices[ii++] = iBL;

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

}
