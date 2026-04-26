using CowColonySim.Game.Render;
using CowColonySim.Sim;
using CowColonySim.Sim.Terrain;
using CowColonySim.Sim.Zones;
using Godot;

namespace CowColonySim.Game.UI;

// Translucent terrain-hugging overlay showing the live tile-rect a
// placement tool is currently dragging out. The owning tool sets
// PreviewRect each frame and clears it on commit/cancel. Mesh is rebuilt
// only when the rect dims or the heightfield revision change.
public partial class RectDragOverlay : Node3D
{
    private const float HoverUnits = 0.5f;

    private Heightfield _field = null!;
    private float _unitsPerMeter;
    private MeshInstance3D _mesh = null!;
    private StandardMaterial3D _material = null!;
    private int _cachedRectKey;
    private int _cachedHeightVersion = -1;
    private Color _appliedColor;

    public TileRect? PreviewRect { get; set; }
    public Color QuadColor { get; set; } = new Color(1f, 0.85f, 0.25f, 0.30f);

    public void Configure(Heightfield field) => _field = field;

    public override void _Ready()
    {
        _unitsPerMeter = SimConstants.GodotUnitsPerTile / SimConstants.MetersPerTile;
        _material = new StandardMaterial3D
        {
            AlbedoColor = QuadColor,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        _appliedColor = QuadColor;
        _mesh = new MeshInstance3D
        {
            MaterialOverride = _material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        AddChild(_mesh);
    }

    public override void _Process(double delta)
    {
        if (PreviewRect is null)
        {
            _mesh.Visible = false;
            return;
        }

        if (_appliedColor != QuadColor)
        {
            _material.AlbedoColor = QuadColor;
            _appliedColor = QuadColor;
        }

        var r = PreviewRect.Value;
        var rectKey = HashCode.Combine(r.MinX, r.MinY, r.MaxX, r.MaxY);
        if (rectKey != _cachedRectKey || _field.Version != _cachedHeightVersion)
        {
            _mesh.Mesh = TerrainStripMesh.Build(
                _field, _unitsPerMeter,
                r.MinX, r.MinY, r.MaxX, r.MaxY,
                HoverUnits);
            _cachedRectKey = rectKey;
            _cachedHeightVersion = _field.Version;
        }
        _mesh.Visible = true;
    }
}
