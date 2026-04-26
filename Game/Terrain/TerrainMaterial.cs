using Godot;

namespace CowColonySim.Game.Terrain;

// Single shared StandardMaterial3D for all terrain (main + LOD). Lighting
// shape is driven by smooth per-vertex normals baked at mesh-build time
// (see TerrainMeshBuilder); the material itself is just a flat green base
// + roughness, no shader gymnastics.
public static class TerrainMaterial
{
    private static StandardMaterial3D? _cached;

    public static StandardMaterial3D Get()
    {
        if (_cached is not null) return _cached;
        _cached = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.42f, 0.55f, 0.32f),
            Roughness = 0.95f,
            Metallic = 0.0f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        return _cached;
    }
}
