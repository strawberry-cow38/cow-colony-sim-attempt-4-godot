using CowColonySim.Sim;
using CowColonySim.Sim.Blueprints;
using CowColonySim.Sim.Terrain;
using Godot;

namespace CowColonySim.Game.UI;

// Translucent footprint-shaped box hovering at the cursor while a
// blueprint placement tool is active. Owning tool sets DefId + Rotation
// + OriginTile each frame. Color flips to red when Valid is false.
public partial class BlueprintGhostPreview : Node3D
{
    private const float HeightMeters = 1.5f;
    private const float HoverUnits = 0.4f;

    private Heightfield _field = null!;
    private float _unitsPerMeter;
    private MeshInstance3D _box = null!;

    public string? DefId { get; set; }
    public int OriginTileX { get; set; }
    public int OriginTileY { get; set; }
    public int RotationSteps { get; set; }
    public bool Valid { get; set; } = true;

    public void Configure(Heightfield field) => _field = field;

    public override void _Ready()
    {
        _unitsPerMeter = SimConstants.GodotUnitsPerTile / SimConstants.MetersPerTile;
        _box = new MeshInstance3D
        {
            Mesh = new BoxMesh
            {
                Size = Vector3.One,
                Material = new StandardMaterial3D
                {
                    AlbedoColor = new Color(0.3f, 0.55f, 0.95f, 0.45f),
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                },
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        AddChild(_box);
    }

    public override void _Process(double delta)
    {
        if (string.IsNullOrEmpty(DefId) || !BlueprintCatalog.TryGet(DefId, out var def) || def is null)
        {
            _box.Visible = false;
            return;
        }

        var (footW, footH) = (RotationSteps & 1) == 0 ? (def.FootprintW, def.FootprintH) : (def.FootprintH, def.FootprintW);
        var unitsPerTile = SimConstants.GodotUnitsPerTile;
        var sizeUnitsX = footW * unitsPerTile;
        var sizeUnitsZ = footH * unitsPerTile;
        var sizeUnitsY = HeightMeters * _unitsPerMeter;

        var mesh = (BoxMesh)_box.Mesh;
        mesh.Size = new Vector3(sizeUnitsX, sizeUnitsY, sizeUnitsZ);

        var mat = (StandardMaterial3D)mesh.Material;
        var target = Valid ? new Color(0.3f, 0.55f, 0.95f, 0.45f) : new Color(0.95f, 0.25f, 0.20f, 0.45f);
        if (mat.AlbedoColor != target) mat.AlbedoColor = target;

        var centerTileX = OriginTileX + footW * 0.5f;
        var centerTileY = OriginTileY + footH * 0.5f;
        var ground = _field.SurfaceMetresAt(centerTileX, centerTileY) * _unitsPerMeter;
        _box.Position = new Vector3(
            centerTileX * unitsPerTile,
            ground + sizeUnitsY * 0.5f + HoverUnits,
            centerTileY * unitsPerTile);
        _box.Visible = true;
    }
}
