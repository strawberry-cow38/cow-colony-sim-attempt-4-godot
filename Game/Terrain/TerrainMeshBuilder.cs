using CowColonySim.Sim;
using CowColonySim.Sim.Terrain;
using Godot;

namespace CowColonySim.Game.Terrain;

// Shared-corner heightfield mesh: one vertex per grid corner, area-weighted
// smoothed normals so each vertex's normal blends adjacent face normals.
// This gives directional-shadow self-shadowing room to breathe — flat-shaded
// per-tile normals make whole quads alias into shadow at grazing angles.
public static class TerrainMeshBuilder
{
    public static ArrayMesh Build(Heightfield field)
    {
        var vw = field.VertWidth;
        var vh = field.VertHeight;
        var tilesX = vw - 1;
        var tilesY = vh - 1;

        var unitsPerTile = SimConstants.GodotUnitsPerTile;
        var unitsPerQuanta = TerrainConstants.VerticalQuantumMetres
                           * (SimConstants.GodotUnitsPerTile / SimConstants.MetersPerTile);

        var vertCount = vw * vh;
        var verts = new Vector3[vertCount];
        var normals = new Vector3[vertCount];
        var uvs = new Vector2[vertCount];

        for (var y = 0; y < vh; y++)
        {
            for (var x = 0; x < vw; x++)
            {
                var i = y * vw + x;
                verts[i] = new Vector3(
                    x * unitsPerTile,
                    field.Get(x, y) * unitsPerQuanta,
                    y * unitsPerTile);
                uvs[i] = new Vector2(x, y);
            }
        }

        var indices = new int[tilesX * tilesY * 6];
        var ii = 0;

        for (var ty = 0; ty < tilesY; ty++)
        {
            for (var tx = 0; tx < tilesX; tx++)
            {
                var iTL = ty * vw + tx;
                var iTR = iTL + 1;
                var iBL = (ty + 1) * vw + tx;
                var iBR = iBL + 1;

                indices[ii++] = iTL;
                indices[ii++] = iBL;
                indices[ii++] = iTR;
                indices[ii++] = iTR;
                indices[ii++] = iBL;
                indices[ii++] = iBR;

                // Unnormalized face normals: |cross| = 2*area, so accumulating
                // raw crosses gives area-weighted vertex normals for free.
                var fn1 = (verts[iBL] - verts[iTL]).Cross(verts[iTR] - verts[iTL]);
                var fn2 = (verts[iBR] - verts[iTR]).Cross(verts[iBL] - verts[iTR]);

                normals[iTL] += fn1;
                normals[iBL] += fn1 + fn2;
                normals[iTR] += fn1 + fn2;
                normals[iBR] += fn2;
            }
        }

        for (var i = 0; i < vertCount; i++)
        {
            normals[i] = normals[i].Normalized();
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
