using CowColonySim.Sim;
using CowColonySim.Sim.Terrain;
using CowColonySim.Sim.Zones;
using Godot;

namespace CowColonySim.Game.UI;

// Translucent ground-quad showing the live tile-rect a placement tool
// is currently dragging out. The owning tool sets PreviewRect each frame
// and clears it on commit/cancel. Quad floats just above the highest
// corner of the rect so it doesn't z-fight on hills.
public partial class RectDragOverlay : Node3D
{
    private const float HoverUnits = 0.5f;

    private Heightfield _field = null!;
    private float _unitsPerMeter;
    private MeshInstance3D _quad = null!;

    public TileRect? PreviewRect { get; set; }
    public Color QuadColor { get; set; } = new Color(1f, 0.85f, 0.25f, 0.30f);

    public void Configure(Heightfield field) => _field = field;

    public override void _Ready()
    {
        _unitsPerMeter = SimConstants.GodotUnitsPerTile / SimConstants.MetersPerTile;
        _quad = new MeshInstance3D
        {
            Mesh = new PlaneMesh
            {
                Size = new Vector2(1f, 1f),
                Material = new StandardMaterial3D
                {
                    AlbedoColor = QuadColor,
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                },
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        AddChild(_quad);
    }

    public override void _Process(double delta)
    {
        if (PreviewRect is null)
        {
            _quad.Visible = false;
            return;
        }
        var r = PreviewRect.Value;
        var unitsPerTile = SimConstants.GodotUnitsPerTile;
        var widthTiles = r.MaxX - r.MinX + 1;
        var heightTiles = r.MaxY - r.MinY + 1;
        var plane = (PlaneMesh)_quad.Mesh;
        plane.Size = new Vector2(widthTiles * unitsPerTile, heightTiles * unitsPerTile);

        var mat = (StandardMaterial3D)plane.Material;
        if (mat.AlbedoColor != QuadColor) mat.AlbedoColor = QuadColor;

        var centerX = (r.MinX + r.MaxX + 1) * 0.5f * unitsPerTile;
        var centerZ = (r.MinY + r.MaxY + 1) * 0.5f * unitsPerTile;
        var topY = MaxCornerHeight(r);
        _quad.Position = new Vector3(centerX, topY + HoverUnits, centerZ);
        _quad.Visible = true;
    }

    private float MaxCornerHeight(TileRect r)
    {
        var corners = new (int vx, int vy)[]
        {
            (r.MinX, r.MinY), (r.MaxX + 1, r.MinY),
            (r.MinX, r.MaxY + 1), (r.MaxX + 1, r.MaxY + 1),
        };
        var max = float.NegativeInfinity;
        foreach (var (vx, vy) in corners)
        {
            var h = _field.SurfaceMetresAt(vx, vy) * _unitsPerMeter;
            if (h > max) max = h;
        }
        return max;
    }
}
