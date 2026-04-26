using CowColonySim.Sim.Terrain;
using Godot;

namespace CowColonySim.Game.Terrain;

// Main-cell terrain rendered as a grid of fixed-size chunks. Each chunk
// is its own MeshInstance3D so an in-game terrain edit only rebuilds the
// chunks intersecting the dirty bbox, not the whole 256x256 surface.
//
// Chunks share the underlying Heightfield by reference — adjacent chunks
// read the same corner verts, so seams stay gap-free even when only one
// side rebuilds.
public partial class ChunkedTerrainRenderer : Node3D
{
    private const int TilesPerChunk = 32;

    private Heightfield _field = null!;
    private MeshInstance3D[,] _chunks = null!;
    private int _chunksX;
    private int _chunksY;

    public void Build(Heightfield field)
    {
        _field = field;
        var tilesX = field.VertWidth - 1;
        var tilesY = field.VertHeight - 1;
        _chunksX = (tilesX + TilesPerChunk - 1) / TilesPerChunk;
        _chunksY = (tilesY + TilesPerChunk - 1) / TilesPerChunk;
        _chunks = new MeshInstance3D[_chunksX, _chunksY];

        for (var cy = 0; cy < _chunksY; cy++)
        {
            for (var cx = 0; cx < _chunksX; cx++)
            {
                var mi = new MeshInstance3D
                {
                    Name = $"Chunk_{cx}_{cy}",
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                    MaterialOverride = TerrainMaterial.Get(),
                };
                AddChild(mi);
                _chunks[cx, cy] = mi;
                RebuildChunk(cx, cy);
            }
        }
    }

    // Rebuild every chunk overlapping the given vertex bbox (inclusive).
    public void RebuildVertexBbox(int minVx, int minVy, int maxVx, int maxVy)
    {
        // A vert (vx,vy) is a corner of tiles tx in [vx-1, vx], ty in [vy-1, vy].
        var tilesX = _field.VertWidth - 1;
        var tilesY = _field.VertHeight - 1;
        var tMinX = Math.Max(0, minVx - 1);
        var tMinY = Math.Max(0, minVy - 1);
        var tMaxX = Math.Min(tilesX - 1, maxVx);
        var tMaxY = Math.Min(tilesY - 1, maxVy);
        if (tMinX > tMaxX || tMinY > tMaxY) return;

        var cMinX = tMinX / TilesPerChunk;
        var cMinY = tMinY / TilesPerChunk;
        var cMaxX = Math.Min(_chunksX - 1, tMaxX / TilesPerChunk);
        var cMaxY = Math.Min(_chunksY - 1, tMaxY / TilesPerChunk);
        for (var cy = cMinY; cy <= cMaxY; cy++)
        {
            for (var cx = cMinX; cx <= cMaxX; cx++)
            {
                RebuildChunk(cx, cy);
            }
        }
    }

    private void RebuildChunk(int cx, int cy)
    {
        var tilesX = _field.VertWidth - 1;
        var tilesY = _field.VertHeight - 1;
        var tileMinX = cx * TilesPerChunk;
        var tileMinY = cy * TilesPerChunk;
        var tileMaxX = Math.Min(tilesX, tileMinX + TilesPerChunk);
        var tileMaxY = Math.Min(tilesY, tileMinY + TilesPerChunk);
        _chunks[cx, cy].Mesh = TerrainMeshBuilder.BuildRange(
            _field, tileMinX, tileMinY, tileMaxX, tileMaxY);
    }
}
