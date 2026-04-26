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
            AlbedoTexture = LoadGrass(),
            Uv1Scale = new Vector3(1f, 1f, 1f),
            Roughness = 0.95f,
            Metallic = 0.0f,
            CullMode = BaseMaterial3D.CullModeEnum.Front,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
        };
        return _cached;
    }

    // Source-pull launcher means assets ship as raw files (no .import). Read
    // the JPG straight off disk and build an ImageTexture so we don't need
    // Godot's import pipeline to have run.
    private static ImageTexture LoadGrass()
    {
        var path = ProjectSettings.GlobalizePath("res://assets/grass05.jpg");
        var img = new Image();
        var err = img.Load(path);
        if (err != Error.Ok) GD.PushError($"Failed to load grass texture at {path}: {err}");
        img.GenerateMipmaps();
        return ImageTexture.CreateFromImage(img);
    }
}
