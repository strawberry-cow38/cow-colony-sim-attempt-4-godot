using CowColonySim.Game.Terrain;
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
    // Capsule + head clearance — kept in sync with ColonistsRenderer's
    // 1.7m capsule so the badge floats just above the colonist's head no
    // matter how high they're standing (wall top, mid-climb, ladder top).
    private const float CapsuleHeightMeters = 1.7f;
    private const float HeadGapMeters = 0.4f;
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

        var headOffsetUnits = (CapsuleHeightMeters + HeadGapMeters) * _unitsPerMeter;
        var topLookup = WalkableTopLookup.Build(snap);
        var slot = 0;
        for (var i = 0; i < colonists.Count; i++)
        {
            var c = colonists[i];
            if (!c.Drafted) continue;
            var x = c.MetersX * _unitsPerMeter;
            var z = c.MetersY * _unitsPerMeter;
            var feetY = WalkableFloor.FeetUnits(_heightfield, _unitsPerMeter, c.MetersX, c.MetersY, c.MetersZ, topLookup);
            var pos = new Vector3(x, feetY + headOffsetUnits, z);
            Multimesh.SetInstanceTransform(slot++, new Transform3D(Basis.Identity, pos));
        }
    }
}
