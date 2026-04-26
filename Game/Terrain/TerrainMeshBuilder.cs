using CowColonySim.Sim;
using CowColonySim.Sim.Terrain;
using Godot;

namespace CowColonySim.Game.Terrain;

// Faceted per-tile mesh: 4 unshared corners per tile, one flat normal per
// tile. This is the locked AoE2 blocky look — do NOT weld verts or smooth
// normals as part of any "smooth lighting" or shadow fix. If shadow acne
// appears at grazing angles, kill terrain shadow casting (CastShadow=Off)
// and let only props/walls cast.
public static class TerrainMeshBuilder
{
    public static ArrayMesh Build(Heightfield field)
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

        var unitsPerTile = SimConstants.GodotUnitsPerTile;
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

                var pTL = new Vector3(x0, hTL, z0);
                var pTR = new Vector3(x1, hTR, z0);
                var pBL = new Vector3(x0, hBL, z1);
                var pBR = new Vector3(x1, hBR, z1);

                var n1 = (pBL - pTL).Cross(pTR - pTL).Normalized();
                var n2 = (pBR - pTR).Cross(pBL - pTR).Normalized();
                var n = (n1 + n2).Normalized();

                var iTL = vi;
                var iTR = vi + 1;
                var iBL = vi + 2;
                var iBR = vi + 3;

                verts[iTL] = pTL; normals[iTL] = n; uvs[iTL] = new Vector2(0f, 0f);
                verts[iTR] = pTR; normals[iTR] = n; uvs[iTR] = new Vector2(1f, 0f);
                verts[iBL] = pBL; normals[iBL] = n; uvs[iBL] = new Vector2(0f, 1f);
                verts[iBR] = pBR; normals[iBR] = n; uvs[iBR] = new Vector2(1f, 1f);

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
}
