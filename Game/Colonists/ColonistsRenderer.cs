using CowColonySim.Sim;
using CowColonySim.Sim.Snapshots;
using CowColonySim.Sim.Terrain;
using Godot;

namespace CowColonySim.Game.Colonists;

// Reads the latest SimSnapshot every frame and updates a MultiMesh of
// capsule transforms. Vertical position sampled from the heightfield at
// each colonist's ground (X, Z).
public partial class ColonistsRenderer : MultiMeshInstance3D
{
    private const float CapsuleRadiusMeters = 0.25f;
    private const float CapsuleHeightMeters = 1.7f;

    private SnapshotPublisher _publisher = null!;
    private Heightfield _heightfield = null!;
    private float _unitsPerMeter;

    public void Configure(SnapshotPublisher publisher, Heightfield heightfield)
    {
        _publisher = publisher;
        _heightfield = heightfield;
    }

    public override void _Ready()
    {
        _unitsPerMeter = SimConstants.GodotUnitsPerTile / SimConstants.MetersPerTile;

        var capsule = new CapsuleMesh
        {
            Radius = CapsuleRadiusMeters * _unitsPerMeter,
            Height = CapsuleHeightMeters * _unitsPerMeter,
            RadialSegments = 12,
            Rings = 4,
            Material = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.95f, 0.85f, 0.55f),
                Roughness = 0.7f,
            },
        };

        Multimesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = capsule,
            InstanceCount = 0,
        };
        CastShadow = ShadowCastingSetting.On;
    }

    public override void _Process(double delta)
    {
        var snap = _publisher.Current;
        var colonists = snap.Colonists;

        if (Multimesh.InstanceCount != colonists.Count)
        {
            Multimesh.InstanceCount = colonists.Count;
        }

        var halfHeightUnits = CapsuleHeightMeters * 0.5f * _unitsPerMeter;
        for (var i = 0; i < colonists.Count; i++)
        {
            var c = colonists[i];
            var x = c.MetersX * _unitsPerMeter;
            var z = c.MetersY * _unitsPerMeter;
            var groundY = SampleGround(c.MetersX, c.MetersY);
            var pos = new Vector3(x, groundY + halfHeightUnits, z);
            Multimesh.SetInstanceTransform(i, new Transform3D(Basis.Identity, pos));
        }
    }

    private float SampleGround(float metersX, float metersY)
    {
        var tilesX = metersX / SimConstants.MetersPerTile;
        var tilesY = metersY / SimConstants.MetersPerTile;
        return _heightfield.SurfaceMetresAt(tilesX, tilesY) * _unitsPerMeter;
    }
}
