using CowColonySim.Sim;
using CowColonySim.Sim.Snapshots;
using CowColonySim.Sim.Terrain;
using Godot;

namespace CowColonySim.Game.Input;

// Flat torus ring that hovers under the currently selected colonist.
// Hidden when nothing is selected. Reads SelectionService for the id
// and the snapshot for the colonist's current position.
public partial class SelectionRing : MeshInstance3D
{
    private const float RingRadiusMeters = 0.6f;
    private const float RingThicknessMeters = 0.05f;

    private SelectionService _selection = null!;
    private SnapshotPublisher _publisher = null!;
    private Heightfield _heightfield = null!;
    private float _unitsPerMeter;
    private float _unitsPerQuanta;

    public void Configure(SelectionService selection, SnapshotPublisher publisher, Heightfield heightfield)
    {
        _selection = selection;
        _publisher = publisher;
        _heightfield = heightfield;
    }

    public override void _Ready()
    {
        _unitsPerMeter = SimConstants.GodotUnitsPerTile / SimConstants.MetersPerTile;
        _unitsPerQuanta = TerrainConstants.VerticalQuantumMetres * _unitsPerMeter;

        Mesh = new TorusMesh
        {
            InnerRadius = (RingRadiusMeters - RingThicknessMeters) * _unitsPerMeter,
            OuterRadius = RingRadiusMeters * _unitsPerMeter,
            RingSegments = 32,
            RadialSegments = 8,
            Material = new StandardMaterial3D
            {
                AlbedoColor = new Color(1f, 0.85f, 0.2f),
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            },
        };
        CastShadow = ShadowCastingSetting.Off;
        Visible = false;
    }

    public override void _Process(double delta)
    {
        if (_selection.SelectedEntityId is not int selId)
        {
            Visible = false;
            return;
        }
        var snap = _publisher.Current;
        for (var i = 0; i < snap.Colonists.Count; i++)
        {
            var c = snap.Colonists[i];
            if (c.EntityId != selId) continue;
            var x = c.MetersX * _unitsPerMeter;
            var z = c.MetersY * _unitsPerMeter;
            var y = SampleGroundUnits(c.MetersX, c.MetersY) + 1f;
            Position = new Vector3(x, y, z);
            Visible = true;
            return;
        }
        Visible = false;
    }

    private float SampleGroundUnits(float metersX, float metersY)
    {
        var tilesX = metersX / SimConstants.MetersPerTile;
        var tilesY = metersY / SimConstants.MetersPerTile;
        var vx = Mathf.Clamp((int)MathF.Round(tilesX), 0, _heightfield.VertWidth - 1);
        var vy = Mathf.Clamp((int)MathF.Round(tilesY), 0, _heightfield.VertHeight - 1);
        return _heightfield.Get(vx, vy) * _unitsPerQuanta;
    }
}
