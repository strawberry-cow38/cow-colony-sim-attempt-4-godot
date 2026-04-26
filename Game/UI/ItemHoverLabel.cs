using CowColonySim.Game.Terrain;
using CowColonySim.Sim;
using CowColonySim.Sim.Items;
using CowColonySim.Sim.Snapshots;
using CowColonySim.Sim.Terrain;
using Godot;

namespace CowColonySim.Game.UI;

// Floating "wood × 5" label that pops up when the cursor hovers near
// any dropped item stack. Uses a 1.2m XZ radius around the cursor's
// ground projection so you don't have to land the mouse exactly on
// the pile. Label3D is billboarded so it always faces the camera.
public partial class ItemHoverLabel : Node3D
{
    private const float HoverRadiusMeters = 1.2f;
    private const float LabelHeightMeters = 0.6f;

    private SnapshotPublisher _publisher = null!;
    private Heightfield _heightfield = null!;
    private float _unitsPerMeter;
    private Label3D _label = null!;

    public void Configure(SnapshotPublisher publisher, Heightfield heightfield)
    {
        _publisher = publisher;
        _heightfield = heightfield;
    }

    public override void _Ready()
    {
        _unitsPerMeter = SimConstants.GodotUnitsPerTile / SimConstants.MetersPerTile;
        _label = new Label3D
        {
            Text = string.Empty,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            FixedSize = true,
            PixelSize = 0.0009f,
            FontSize = 36,
            OutlineSize = 6,
            Modulate = new Color(1f, 1f, 1f),
            OutlineModulate = new Color(0f, 0f, 0f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_label);
        _label.Visible = false;
    }

    public override void _Process(double delta)
    {
        var camera = GetViewport().GetCamera3D();
        if (camera is null) { _label.Visible = false; return; }

        var mouse = GetViewport().GetMousePosition();
        var groundHit = TerrainRayCast.Project(camera, mouse, _heightfield);
        if (groundHit is null) { _label.Visible = false; return; }

        var hit = groundHit.Value;
        var snap = _publisher.Current;
        var bestId = -1;
        var bestDistSqUnits = float.PositiveInfinity;
        var radiusUnits = HoverRadiusMeters * _unitsPerMeter;
        var radiusSqUnits = radiusUnits * radiusUnits;

        ItemView? best = null;
        for (var i = 0; i < snap.Items.Count; i++)
        {
            var it = snap.Items[i];
            var metersX = (it.TileX + 0.5f) * SimConstants.MetersPerTile;
            var metersY = (it.TileY + 0.5f) * SimConstants.MetersPerTile;
            var ix = metersX * _unitsPerMeter;
            var iz = metersY * _unitsPerMeter;
            var dx = ix - hit.X;
            var dz = iz - hit.Z;
            var d = dx * dx + dz * dz;
            if (d > radiusSqUnits) continue;
            if (d >= bestDistSqUnits) continue;
            bestDistSqUnits = d;
            bestId = it.EntityId;
            best = it;
        }

        if (best is null || bestId == -1) { _label.Visible = false; return; }
        var view = best.Value;

        var bx = (view.TileX + 0.5f) * SimConstants.MetersPerTile;
        var by = (view.TileY + 0.5f) * SimConstants.MetersPerTile;
        var groundY = _heightfield.SurfaceMetresAt(
            bx / SimConstants.MetersPerTile,
            by / SimConstants.MetersPerTile) * _unitsPerMeter;
        Position = new Vector3(
            bx * _unitsPerMeter,
            groundY + LabelHeightMeters * _unitsPerMeter,
            by * _unitsPerMeter);

        var prefix = view.Forbidden ? "[forbidden] " : "";
        _label.Text = $"{prefix}{KindLabel(view.Kind)} ×{view.Count}";
        _label.Visible = true;
    }

    private static string KindLabel(ItemKind kind) => kind switch
    {
        ItemKind.Wood => "wood",
        _ => "item",
    };
}
