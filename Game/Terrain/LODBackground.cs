using CowColonySim.Sim;
using CowColonySim.Sim.Terrain;
using Godot;

namespace CowColonySim.Game.Terrain;

// 8 lower-poly background terrain chunks ringing the main cell. Each
// chunk covers the same world span as main but is sampled at coarse
// stride so the vert count is ~4096 per chunk vs ~262k for main. Heights
// share the main cell's noise field (continuity at chunk seams) by
// offsetting HeightfieldGenerator origin in tile space.
public partial class LODBackground : Node3D
{
    private const int LodVertResolution = 32; // tiles per chunk side at LOD
    private static readonly Vector2I[] Offsets =
    {
        new(-1, -1), new(0, -1), new(1, -1),
        new(-1,  0),             new(1,  0),
        new(-1,  1), new(0,  1), new(1,  1),
    };

    public void Build(int mainCellTiles, HeightfieldGenerator.Settings baseSettings)
    {
        var stride = (float)mainCellTiles / LodVertResolution;
        var lodUnitsPerTile = SimConstants.GodotUnitsPerTile * stride;

        foreach (var off in Offsets)
        {
            var chunk = new Heightfield(LodVertResolution, LodVertResolution);
            HeightfieldGenerator.Generate(chunk, baseSettings with
            {
                OriginTilesX = off.X * mainCellTiles,
                OriginTilesY = off.Y * mainCellTiles,
                TileSpacing = stride,
            });

            var renderer = new TerrainRenderer
            {
                Name = $"LOD_{off.X}_{off.Y}",
                Position = new Vector3(
                    off.X * mainCellTiles * SimConstants.GodotUnitsPerTile,
                    0f,
                    off.Y * mainCellTiles * SimConstants.GodotUnitsPerTile),
            };
            AddChild(renderer);
            renderer.Build(chunk, lodUnitsPerTile);
        }
    }
}
