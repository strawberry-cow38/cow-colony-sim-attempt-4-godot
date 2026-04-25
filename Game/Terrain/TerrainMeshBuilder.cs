using CowColonySim.Sim;
using CowColonySim.Sim.Terrain;
using Godot;

namespace CowColonySim.Game.Terrain;

// Welded vertex grid: one vert per heightfield corner shared across adjacent
// tiles. Per-tile face normals are accumulated into shared corner normals
// then normalized, giving smooth (Gouraud-style) shading across the terrain.
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

        var verts = new Vector3[vw * vh];
        var normals = new Vector3[vw * vh];
        var indices = new int[tilesX * tilesY * 6];

        for (var vy = 0; vy < vh; vy++)
        {
            for (var vx = 0; vx < vw; vx++)
            {
                var h = field.Get(vx, vy) * unitsPerQuanta;
                verts[vy * vw + vx] = new Vector3(vx * unitsPerTile, h, vy * unitsPerTile);
            }
        }

        for (var ty = 0; ty < tilesY; ty++)
        {
            for (var tx = 0; tx < tilesX; tx++)
            {
                var iTL = ty * vw + tx;
                var iTR = ty * vw + tx + 1;
                var iBL = (ty + 1) * vw + tx;
                var iBR = (ty + 1) * vw + tx + 1;

                var n1 = (verts[iBL] - verts[iTL]).Cross(verts[iTR] - verts[iTL]);
                var n2 = (verts[iBR] - verts[iTR]).Cross(verts[iBL] - verts[iTR]);
                var faceSum = n1 + n2;

                normals[iTL] += faceSum;
                normals[iTR] += faceSum;
                normals[iBL] += faceSum;
                normals[iBR] += faceSum;
            }
        }
        for (var i = 0; i < normals.Length; i++)
        {
            normals[i] = normals[i].Normalized();
        }

        var ii = 0;
        for (var ty = 0; ty < tilesY; ty++)
        {
            for (var tx = 0; tx < tilesX; tx++)
            {
                var iTL = ty * vw + tx;
                var iTR = ty * vw + tx + 1;
                var iBL = (ty + 1) * vw + tx;
                var iBR = (ty + 1) * vw + tx + 1;
                indices[ii++] = iTL;
                indices[ii++] = iBL;
                indices[ii++] = iTR;
                indices[ii++] = iTR;
                indices[ii++] = iBL;
                indices[ii++] = iBR;
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
