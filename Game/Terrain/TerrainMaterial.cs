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
        _cached = new StandardMaterial3D
        {
            AlbedoTexture = LoadImageTexture("res://assets/grass05.jpg"),
            NormalEnabled = true,
            NormalTexture = LoadBumpAsNormalMap("res://assets/grass0xb.jpg"),
            NormalScale = 0.6f,
            Uv1Scale = new Vector3(1f, 1f, 1f),
            Roughness = 1.0f,
            Metallic = 0.0f,
            MetallicSpecular = 0.0f,
            SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
            DiffuseMode = BaseMaterial3D.DiffuseModeEnum.Lambert,
            CullMode = BaseMaterial3D.CullModeEnum.Back,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
        };
        return _cached;
    }

    // Source-pull launcher means assets ship as raw files (no .import). Read
    // the JPG straight off disk and build an ImageTexture so we don't need
    // Godot's import pipeline to have run.
    private static ImageTexture LoadImageTexture(string resPath)
    {
        var path = ProjectSettings.GlobalizePath(resPath);
        var img = new Image();
        var err = img.Load(path);
        if (err != Error.Ok) GD.PushError($"Failed to load texture at {path}: {err}");
        img.GenerateMipmaps();
        return ImageTexture.CreateFromImage(img);
    }

    // Source asset is a grayscale bump (height) map; Godot's NormalTexture
    // wants RGB-encoded normals. Convert in place at load time.
    private static ImageTexture LoadBumpAsNormalMap(string resPath)
    {
        var path = ProjectSettings.GlobalizePath(resPath);
        var img = new Image();
        var err = img.Load(path);
        if (err != Error.Ok) GD.PushError($"Failed to load bump map at {path}: {err}");
        img.BumpMapToNormalMap();
        img.GenerateMipmaps();
        return ImageTexture.CreateFromImage(img);
    }
}
