using CowColonySim.Sim;
using Godot;

namespace CowColonySim.Game.Terrain;

// Translucent perimeter slab around the main tile grid. Visual marker only —
// pathfinding is already bounded by HeightGrid.InBounds, so colonists can't
// step or path outside [0, tilesPerSide). Built as four box meshes that
// straddle the outer edge so the inside face sits exactly on the boundary.
public partial class BorderWall : Node3D
{
    private const float WallHeightMeters = 32f;
    private const float WallThicknessMeters = 0.25f;

    public void Build(int tilesPerSide)
    {
        var unitsPerMeter = SimConstants.GodotUnitsPerTile / SimConstants.MetersPerTile;
        var span = tilesPerSide * SimConstants.GodotUnitsPerTile;
        var height = WallHeightMeters * unitsPerMeter;
        var thickness = WallThicknessMeters * unitsPerMeter;

        var mat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.30f, 0.30f, 0.30f, 0.30f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Roughness = 1.0f,
            Metallic = 0.0f,
            MetallicSpecular = 0.0f,
            SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };

        var halfSpan = span * 0.5f;
        var halfH = height * 0.5f;
        var halfT = thickness * 0.5f;
        var longSize = new Vector3(span + thickness, height, thickness);
        var shortSize = new Vector3(thickness, height, span + thickness);

        AddWall("North", new Vector3(halfSpan, halfH, -halfT), longSize, mat);
        AddWall("South", new Vector3(halfSpan, halfH, span + halfT), longSize, mat);
        AddWall("West",  new Vector3(-halfT, halfH, halfSpan), shortSize, mat);
        AddWall("East",  new Vector3(span + halfT, halfH, halfSpan), shortSize, mat);
    }

    private void AddWall(string name, Vector3 center, Vector3 size, StandardMaterial3D mat)
    {
        var mi = new MeshInstance3D
        {
            Name = name,
            Mesh = new BoxMesh { Size = size },
            MaterialOverride = mat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Position = center,
        };
        AddChild(mi);
    }
}
