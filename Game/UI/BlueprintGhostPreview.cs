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
    private const float LayerStepMeters = 0.75f;
    private const float HoverUnits = 0.4f;

    private Heightfield _field = null!;
    private float _unitsPerMeter;
    private MeshInstance3D _box = null!;
    private readonly List<MeshInstance3D> _reservationTiles = new();

    public string? DefId { get; set; }
    public int OriginTileX { get; set; }
    public int OriginTileY { get; set; }
    public int RotationSteps { get; set; }
    public int BaseLayer { get; set; }
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
            HideReservations();
            return;
        }

        var (footW, footH) = (RotationSteps & 1) == 0 ? (def.FootprintW, def.FootprintH) : (def.FootprintH, def.FootprintW);
        var unitsPerTile = SimConstants.GodotUnitsPerTile;
        var sizeUnitsX = footW * unitsPerTile;
        var sizeUnitsZ = footH * unitsPerTile;
        var sizeUnitsY = def.HeightMeters * _unitsPerMeter;

        var mesh = (BoxMesh)_box.Mesh;
        mesh.Size = new Vector3(sizeUnitsX, sizeUnitsY, sizeUnitsZ);

        var mat = (StandardMaterial3D)mesh.Material;
        var target = Valid ? new Color(0.3f, 0.55f, 0.95f, 0.45f) : new Color(0.95f, 0.25f, 0.20f, 0.45f);
        if (mat.AlbedoColor != target) mat.AlbedoColor = target;

        var centerTileX = OriginTileX + footW * 0.5f;
        var centerTileY = OriginTileY + footH * 0.5f;
        var ground = _field.SurfaceMetresAt(centerTileX, centerTileY) * _unitsPerMeter;
        var layerOffset = BaseLayer * LayerStepMeters * _unitsPerMeter;
        _box.Position = new Vector3(
            centerTileX * unitsPerTile,
            ground + layerOffset + sizeUnitsY * 0.5f + HoverUnits,
            centerTileY * unitsPerTile);
        _box.Visible = true;

        DrawReservations(def, RotationSteps, OriginTileX, OriginTileY);
    }

    private void DrawReservations(BlueprintDef def, int rot, int originX, int originY)
    {
        var reqs = def.Requirements;
        EnsureReservationCount(reqs.Count);
        for (var i = 0; i < reqs.Count; i++)
        {
            var r = reqs[i];
            var (offX, offY) = RotateOffset(r.OffsetX, r.OffsetY, rot);
            var tx = originX + offX;
            var ty = originY + offY;
            var unitsPerTile = SimConstants.GodotUnitsPerTile;
            var tile = _reservationTiles[i];
            var mat = (StandardMaterial3D)((BoxMesh)tile.Mesh).Material;
            mat.AlbedoColor = ColorFor(r.Kind);
            var ground = _field.SurfaceMetresAt(tx + 0.5f, ty + 0.5f) * _unitsPerMeter;
            tile.Position = new Vector3((tx + 0.5f) * unitsPerTile, ground + 1.5f, (ty + 0.5f) * unitsPerTile);
            tile.Visible = true;
        }
        for (var i = reqs.Count; i < _reservationTiles.Count; i++)
        {
            _reservationTiles[i].Visible = false;
        }
    }

    private void HideReservations()
    {
        for (var i = 0; i < _reservationTiles.Count; i++) _reservationTiles[i].Visible = false;
    }

    private void EnsureReservationCount(int count)
    {
        while (_reservationTiles.Count < count)
        {
            var unitsPerTile = SimConstants.GodotUnitsPerTile;
            var tile = new MeshInstance3D
            {
                Mesh = new BoxMesh
                {
                    Size = new Vector3(unitsPerTile * 0.85f, 1.5f, unitsPerTile * 0.85f),
                    Material = new StandardMaterial3D
                    {
                        AlbedoColor = new Color(0.3f, 0.85f, 0.85f, 0.55f),
                        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                    },
                },
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Visible = false,
            };
            _reservationTiles.Add(tile);
            AddChild(tile);
        }
    }

    private static (int x, int y) RotateOffset(int x, int y, int rot)
    {
        return (rot & 3) switch
        {
            1 => (-y, x),
            2 => (-x, -y),
            3 => (y, -x),
            _ => (x, y),
        };
    }

    private static Color ColorFor(FootprintRequirementKind kind) => kind switch
    {
        FootprintRequirementKind.InteractionSpot => new Color(0.30f, 0.85f, 0.85f, 0.55f),
        FootprintRequirementKind.VentSide => new Color(0.95f, 0.40f, 0.85f, 0.55f),
        _ => new Color(0.85f, 0.85f, 0.85f, 0.55f),
    };
}
