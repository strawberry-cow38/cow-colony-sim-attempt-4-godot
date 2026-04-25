using CowColonySim.Sim.Terrain;
using Godot;

namespace CowColonySim.Game.Terrain;

public partial class TerrainRenderer : MeshInstance3D
{
    public void Build(Heightfield field)
    {
        Mesh = TerrainMeshBuilder.Build(field);
        // Terrain receives shadows but does not cast: faceted per-tile normals
        // alias whole quads into shadow when the depth test fails at grazing,
        // and the per-tile look is locked, so we let only props cast.
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        var mat = new StandardMaterial3D
        {
            AlbedoColor = Colors.White,
            AlbedoTexture = GrassTexture.Build(),
            Roughness = 0.95f,
            Metallic = 0.0f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            DiffuseMode = BaseMaterial3D.DiffuseModeEnum.LambertWrap,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
            Uv1Triplanar = true,
            Uv1Scale = new Vector3(0.5f, 0.5f, 0.5f),
        };
        MaterialOverride = mat;
    }
}
