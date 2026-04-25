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
            AlbedoColor = Colors.White,
            AlbedoTexture = GrassTexture.Build(),
            Roughness = 0.85f,
            Metallic = 0.0f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
            Uv1Triplanar = true,
            Uv1Scale = new Vector3(0.5f, 0.5f, 0.5f),
        };
        MaterialOverride = mat;
    }
}
