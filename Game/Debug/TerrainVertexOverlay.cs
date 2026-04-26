using CowColonySim.Sim;
using CowColonySim.Sim.Terrain;
using Godot;

namespace CowColonySim.Game.Debug;

// Red dot per heightfield vertex. Hidden by default; toggle with P.
public partial class TerrainVertexOverlay : MultiMeshInstance3D
{
    private const float DotRadius = 1.4f;

    public void Build(Heightfield field)
    {
        var sphere = new SphereMesh
        {
            Radius = DotRadius,
            Height = DotRadius * 2f,
            RadialSegments = 8,
            Rings = 4,
            Material = new StandardMaterial3D
            {
                AlbedoColor = new Color(1f, 0.1f, 0.1f),
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            },
        };

        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = sphere,
            InstanceCount = field.VertWidth * field.VertHeight,
        };

        var unitsPerTile = SimConstants.GodotUnitsPerTile;
        var unitsPerQuanta = TerrainConstants.VerticalQuantumMetres
                           * (SimConstants.GodotUnitsPerTile / SimConstants.MetersPerTile);

        var i = 0;
        for (var vy = 0; vy < field.VertHeight; vy++)
        {
            for (var vx = 0; vx < field.VertWidth; vx++)
            {
                var h = field.Get(vx, vy) * unitsPerQuanta;
                var p = new Vector3(vx * unitsPerTile, h, vy * unitsPerTile);
                mm.SetInstanceTransform(i++, new Transform3D(Basis.Identity, p));
            }
        }

        Multimesh = mm;
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        Visible = false;
    }

    public override void _UnhandledInput(InputEvent ev)
    {
        if (ev is InputEventKey k && k.Pressed && !k.Echo && k.PhysicalKeycode == Key.P)
        {
            Visible = !Visible;
        }
    }
}
