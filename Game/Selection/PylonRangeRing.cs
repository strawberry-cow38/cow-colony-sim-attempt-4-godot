using CowColonySim.Sim;
using CowColonySim.Sim.Blueprints;
using CowColonySim.Sim.Snapshots;
using CowColonySim.Sim.Systems;
using CowColonySim.Sim.Terrain;
using CowColonySim.Sim.World.Components;
using Godot;

namespace CowColonySim.Game.Selection;

// Two flat rings hovering at a selected pylon: a tight selection ring at
// the base + a wide service-radius ring (ServiceRadiusTiles) so the player
// sees what the pylon will power. Hidden unless the current selection is
// a pylon (built structure or blueprint ghost). Coexists with SelectionRing
// — that one only handles colonist/tree/boulder/item, so this node fills
// the structure case + adds the range visual.
public partial class PylonRangeRing : Node3D
{
    private const float SelectionRingRadiusMeters = 0.7f;
    private const float SelectionRingThicknessMeters = 0.06f;
    private const float RangeRingThicknessMeters = 0.18f;

    private SelectionService _selection = null!;
    private SnapshotPublisher _publisher = null!;
    private Heightfield _field = null!;
    private float _unitsPerMeter;
    private MeshInstance3D _selectRing = null!;
    private MeshInstance3D _rangeRing = null!;

    public void Configure(SelectionService selection, SnapshotPublisher publisher, Heightfield field)
    {
        _selection = selection;
        _publisher = publisher;
        _field = field;
    }

    public override void _Ready()
    {
        _unitsPerMeter = SimConstants.GodotUnitsPerTile / SimConstants.MetersPerTile;

        var selectMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 0.85f, 0.2f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        _selectRing = new MeshInstance3D
        {
            Mesh = new TorusMesh
            {
                InnerRadius = (SelectionRingRadiusMeters - SelectionRingThicknessMeters) * _unitsPerMeter,
                OuterRadius = SelectionRingRadiusMeters * _unitsPerMeter,
                RingSegments = 32,
                Material = selectMat,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        AddChild(_selectRing);

        var rangeMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.3f, 0.95f, 0.6f, 0.55f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        var radiusMeters = PowerSystem.ServiceRadiusTiles * SimConstants.MetersPerTile;
        _rangeRing = new MeshInstance3D
        {
            Mesh = new TorusMesh
            {
                InnerRadius = (radiusMeters - RangeRingThicknessMeters) * _unitsPerMeter,
                OuterRadius = radiusMeters * _unitsPerMeter,
                RingSegments = 64,
                Material = rangeMat,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        AddChild(_rangeRing);
    }

    public override void _Process(double delta)
    {
        var snap = _publisher.Current;
        if (TryShowFromStructure(snap)) return;
        if (TryShowFromBlueprint(snap)) return;
        _selectRing.Visible = false;
        _rangeRing.Visible = false;
    }

    private bool TryShowFromStructure(SimSnapshot snap)
    {
        if (_selection.SelectedStructureId is not int id) return false;
        for (var i = 0; i < snap.Structures.Count; i++)
        {
            var s = snap.Structures[i];
            if (s.EntityId != id) continue;
            if (!BlueprintCatalog.TryGet(s.DefId, out var def) || def is null) return false;
            if (def.Power != PowerNodeKind.Pylon) return false;
            ShowAt(s.TileX + def.FootprintW * 0.5f, s.TileY + def.FootprintH * 0.5f, s.BaseLayer);
            return true;
        }
        return false;
    }

    private bool TryShowFromBlueprint(SimSnapshot snap)
    {
        if (_selection.SelectedBlueprintId is not int id) return false;
        for (var i = 0; i < snap.BlueprintGhosts.Count; i++)
        {
            var g = snap.BlueprintGhosts[i];
            if (g.EntityId != id) continue;
            if (!BlueprintCatalog.TryGet(g.DefId, out var def) || def is null) return false;
            if (def.Power != PowerNodeKind.Pylon) return false;
            ShowAt(g.OriginTileX + def.FootprintW * 0.5f, g.OriginTileY + def.FootprintH * 0.5f, g.BaseLayer);
            return true;
        }
        return false;
    }

    private void ShowAt(float centerTileX, float centerTileY, int baseLayer)
    {
        var x = centerTileX * SimConstants.GodotUnitsPerTile;
        var z = centerTileY * SimConstants.GodotUnitsPerTile;
        var ground = _field.SurfaceMetresAt(centerTileX, centerTileY) * _unitsPerMeter;
        var stack = baseLayer * 0.75f * _unitsPerMeter;
        var y = ground + stack + 0.6f;
        _selectRing.Position = new Vector3(x, y, z);
        _rangeRing.Position = new Vector3(x, y, z);
        _selectRing.Visible = true;
        _rangeRing.Visible = true;
    }
}
