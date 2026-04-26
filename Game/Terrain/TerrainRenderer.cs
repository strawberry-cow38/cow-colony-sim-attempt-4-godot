using CowColonySim.Sim.Terrain;
using Godot;

namespace CowColonySim.Game.Terrain;

public partial class TerrainRenderer : MeshInstance3D
{
    public void Build(Heightfield field, float? unitsPerTileOverride = null)
    {
        Mesh = TerrainMeshBuilder.Build(field, unitsPerTileOverride);
        // Faceted per-tile normals self-shadow-alias on slopes; only let
        // props/walls cast. Terrain still receives shadows.
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        MaterialOverride = TerrainMaterial.Get();
    }
}
