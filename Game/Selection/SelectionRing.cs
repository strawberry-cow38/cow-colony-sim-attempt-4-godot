using CowColonySim.Sim;
using CowColonySim.Sim.Snapshots;
using CowColonySim.Sim.Terrain;
using Godot;

namespace CowColonySim.Game.Selection;

// Flat torus ring that hovers under the currently selected entity.
// Hidden when nothing is selected. Boulders use a wider ring at the
// same y so the ring isn't hidden under the rock; the mesh swaps based
// on selection kind.
public partial class SelectionRing : MeshInstance3D
{
    private const float RingRadiusMeters = 0.6f;
    private const float RingThicknessMeters = 0.05f;
    private const float BoulderRingRadiusMeters = 1.1f;
    private const float BoulderRingThicknessMeters = 0.06f;

    private SelectionService _selection = null!;
    private SnapshotPublisher _publisher = null!;
    private Heightfield _heightfield = null!;
    private float _unitsPerMeter;
    private TorusMesh _defaultMesh = null!;
    private TorusMesh _boulderMesh = null!;

    public void Configure(SelectionService selection, SnapshotPublisher publisher, Heightfield heightfield)
    {
        _selection = selection;
        _publisher = publisher;
        _heightfield = heightfield;
    }

    public override void _Ready()
    {
        _unitsPerMeter = SimConstants.GodotUnitsPerTile / SimConstants.MetersPerTile;

        var material = new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 0.85f, 0.2f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        _defaultMesh = new TorusMesh
        {
            InnerRadius = (RingRadiusMeters - RingThicknessMeters) * _unitsPerMeter,
            OuterRadius = RingRadiusMeters * _unitsPerMeter,
            RingSegments = 32,
            Material = material,
        };
        _boulderMesh = new TorusMesh
        {
            InnerRadius = (BoulderRingRadiusMeters - BoulderRingThicknessMeters) * _unitsPerMeter,
            OuterRadius = BoulderRingRadiusMeters * _unitsPerMeter,
            RingSegments = 32,
            Material = material,
        };
        Mesh = _defaultMesh;
        CastShadow = ShadowCastingSetting.Off;
        Visible = false;
    }

    public override void _Process(double delta)
    {
        var snap = _publisher.Current;
        if (_selection.SelectedEntityId is int selId)
        {
            for (var i = 0; i < snap.Colonists.Count; i++)
            {
                var c = snap.Colonists[i];
                if (c.EntityId != selId) continue;
                var x = c.MetersX * _unitsPerMeter;
                var z = c.MetersY * _unitsPerMeter;
                var y = SampleGroundUnits(c.MetersX, c.MetersY) + 1f;
                Mesh = _defaultMesh;
                Position = new Vector3(x, y, z);
                Visible = true;
                return;
            }
        }
        if (_selection.SelectedTreeId is int treeId)
        {
            for (var i = 0; i < snap.Trees.Count; i++)
            {
                var t = snap.Trees[i];
                if (t.EntityId != treeId) continue;
                var metersX = (t.TileX + 0.5f) * SimConstants.MetersPerTile;
                var metersY = (t.TileY + 0.5f) * SimConstants.MetersPerTile;
                var x = metersX * _unitsPerMeter;
                var z = metersY * _unitsPerMeter;
                var y = SampleGroundUnits(metersX, metersY) + 1f;
                Mesh = _defaultMesh;
                Position = new Vector3(x, y, z);
                Visible = true;
                return;
            }
        }
        if (_selection.SelectedBoulderId is int boulderId)
        {
            for (var i = 0; i < snap.Boulders.Count; i++)
            {
                var b = snap.Boulders[i];
                if (b.EntityId != boulderId) continue;
                var metersX = (b.TileX + 0.5f) * SimConstants.MetersPerTile;
                var metersY = (b.TileY + 0.5f) * SimConstants.MetersPerTile;
                var x = metersX * _unitsPerMeter;
                var z = metersY * _unitsPerMeter;
                var y = SampleGroundUnits(metersX, metersY) + 1f;
                Mesh = _boulderMesh;
                Position = new Vector3(x, y, z);
                Visible = true;
                return;
            }
        }
        if (_selection.SelectedItemId is int itemId)
        {
            for (var i = 0; i < snap.Items.Count; i++)
            {
                var it = snap.Items[i];
                if (it.EntityId != itemId) continue;
                var metersX = (it.TileX + 0.5f) * SimConstants.MetersPerTile;
                var metersY = (it.TileY + 0.5f) * SimConstants.MetersPerTile;
                var x = metersX * _unitsPerMeter;
                var z = metersY * _unitsPerMeter;
                var y = SampleGroundUnits(metersX, metersY) + 1f;
                Position = new Vector3(x, y, z);
                Visible = true;
                return;
            }
        }
        Visible = false;
    }

    private float SampleGroundUnits(float metersX, float metersY)
    {
        var tilesX = metersX / SimConstants.MetersPerTile;
        var tilesY = metersY / SimConstants.MetersPerTile;
        return _heightfield.SurfaceMetresAt(tilesX, tilesY) * _unitsPerMeter;
    }
}
