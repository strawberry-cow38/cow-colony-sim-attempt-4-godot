using CowColonySim.Sim.Terrain;
using Godot;

namespace CowColonySim.Game.Terrain;

public partial class TerrainRenderer : MeshInstance3D
{
    public void Build(Heightfield field)
    {
        Mesh = TerrainMeshBuilder.Build(field);
        // Faceted per-tile normals self-shadow-alias on slopes; only let
        // props/walls cast. Terrain still receives shadows.
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        MaterialOverride = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.42f, 0.55f, 0.32f),
            Roughness = 0.95f,
            Metallic = 0.0f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
    }
}
