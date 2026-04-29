using CowColonySim.Sim;
using CowColonySim.Sim.Snapshots;
using CowColonySim.Sim.Terrain;
using Godot;

namespace CowColonySim.Game.Colonists;

// Tiny billboarded red square hovering above each drafted colonist's head.
// One MultiMesh, instance count = drafted-count, recomputed per-frame from
// the snapshot. Unshaded so it pops at night.
public partial class DraftedBadgesRenderer : MultiMeshInstance3D
{
    private const float HeadOffsetMeters = 2.1f;
    private const float BadgeSizeMeters = 0.45f;

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

        var quad = new QuadMesh
        {
            Size = new Vector2(BadgeSizeMeters * _unitsPerMeter, BadgeSizeMeters * _unitsPerMeter),
            Material = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.95f, 0.15f, 0.15f),
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
                BillboardKeepScale = true,
                Transparency = BaseMaterial3D.TransparencyEnum.Disabled,
            },
        };

        Multimesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = quad,
            InstanceCount = 0,
        };
        CastShadow = ShadowCastingSetting.Off;
    }

    public override void _Process(double delta)
    {
        var snap = _publisher.Current;
        var colonists = snap.Colonists;

        var draftCount = 0;
        for (var i = 0; i < colonists.Count; i++) if (colonists[i].Drafted) draftCount++;

        if (Multimesh.InstanceCount != draftCount) Multimesh.InstanceCount = draftCount;
        if (draftCount == 0) return;

        var headOffsetUnits = HeadOffsetMeters * _unitsPerMeter;
        var slot = 0;
        for (var i = 0; i < colonists.Count; i++)
        {
            var c = colonists[i];
            if (!c.Drafted) continue;
            var x = c.MetersX * _unitsPerMeter;
            var z = c.MetersY * _unitsPerMeter;
            var groundY = SampleGround(c.MetersX, c.MetersY);
            var pos = new Vector3(x, groundY + headOffsetUnits, z);
            Multimesh.SetInstanceTransform(slot++, new Transform3D(Basis.Identity, pos));
        }
    }

    private float SampleGround(float metersX, float metersY)
    {
        var tilesX = metersX / SimConstants.MetersPerTile;
        var tilesY = metersY / SimConstants.MetersPerTile;
        return _heightfield.SurfaceMetresAt(tilesX, tilesY) * _unitsPerMeter;
    }
}
