using CowColonySim.Sim.Terrain;
using Godot;

namespace CowColonySim.Game.Terrain;

public partial class TerrainRenderer : MeshInstance3D
{
    public void Build(Heightfield field)
    {
        Mesh = TerrainMeshBuilder.Build(field);
        var mat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.227f, 0.478f, 0.227f), // grass green
            Roughness = 0.85f,
            Metallic = 0.0f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        MaterialOverride = mat;
    }
}
