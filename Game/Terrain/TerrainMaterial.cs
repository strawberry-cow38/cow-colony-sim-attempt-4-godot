using Godot;

namespace CowColonySim.Game.Terrain;

// Single shared StandardMaterial3D for all terrain (main + LOD). Lighting
// shape is driven by smooth per-vertex normals baked at mesh-build time
// (see TerrainMeshBuilder); the material is a tileable grass photo.
// Per-tile UVs are 0..1 (each tile is 1.5m); Uv1Scale here = repeats per tile.
public static class TerrainMaterial
{
    private static StandardMaterial3D? _cached;

    public static StandardMaterial3D Get()
    {
        if (_cached is not null) return _cached;
        var tex = GD.Load<Texture2D>("res://assets/grass05.jpg");
        _cached = new StandardMaterial3D
        {
            AlbedoTexture = tex,
            Uv1Scale = new Vector3(2f, 2f, 1f),
            Roughness = 0.95f,
            Metallic = 0.0f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
        };
        return _cached;
    }
}
