using CowColonySim.Sim;
using CowColonySim.Sim.Terrain;
using Godot;

namespace CowColonySim.Game.Terrain;

// Per-tile mesh: 4 unshared corners per tile, ONE flat normal per tile
// (averaged from the two triangle normals). AoE2-style faceted look —
// crisp shading per tile, no smooth interpolation across tile borders.
public static class TerrainMeshBuilder
{
    public static ArrayMesh Build(Heightfield field, float? unitsPerTileOverride = null) =>
        BuildRange(field, 0, 0, field.VertWidth - 1, field.VertHeight - 1, unitsPerTileOverride);

    // Build a mesh covering tiles [tileMinX, tileMaxX) x [tileMinY, tileMaxY).
    // Used by ChunkedTerrainRenderer to rebuild a chunk after dirty edits.
    // Vertex positions come from the same heightfield as the rest of the
    // terrain, so adjacent chunks share corner heights and the seams are
    // gap-free even when only one chunk is rebuilt.
    public static ArrayMesh BuildRange(
        Heightfield field,
        int tileMinX, int tileMinY,
        int tileMaxX, int tileMaxY,
        float? unitsPerTileOverride = null)
    {
        if (tileMinX < 0) tileMinX = 0;
        if (tileMinY < 0) tileMinY = 0;
        if (tileMaxX > field.VertWidth - 1) tileMaxX = field.VertWidth - 1;
        if (tileMaxY > field.VertHeight - 1) tileMaxY = field.VertHeight - 1;

        var tilesX = tileMaxX - tileMinX;
        var tilesY = tileMaxY - tileMinY;
        if (tilesX <= 0 || tilesY <= 0) return new ArrayMesh();
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
        for (var ty = tileMinY; ty < tileMaxY; ty++)
        {
            for (var tx = tileMinX; tx < tileMaxX; tx++)
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

                // Right-hand cross product order chosen so a flat tile gives
                // +Y (up). Last attempt had operands swapped → -Y normals,
                // which zeroed direct light and killed shadow receive.
                var n1 = (pBL - pTL).Cross(pTR - pTL);
                var n2 = (pBL - pTR).Cross(pBR - pTR);
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
