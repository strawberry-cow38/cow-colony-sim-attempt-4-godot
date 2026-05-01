using CowColonySim.Sim;
using CowColonySim.Sim.Blueprints;
using CowColonySim.Sim.Snapshots;
using CowColonySim.Sim.Terrain;
using Godot;

namespace CowColonySim.Game.Render;

// Renders table lamps as a multimesh of the table_lamp.glb mesh. Mirrors
// PylonsRenderer's load+merge pattern. StructuresRenderer skips the
// table_lamp def so the placeholder box doesn't draw on top.
public partial class TableLampRenderer : Node3D
{
    private const float LayerStepMeters = 0.75f;
    private const string DefId = "furniture.table_lamp";

    private SnapshotPublisher _publisher = null!;
    private Heightfield _heightfield = null!;
    private float _unitsPerMeter;
    private MultiMeshInstance3D? _bucket;

    public void Configure(SnapshotPublisher publisher, Heightfield heightfield)
    {
        _publisher = publisher;
        _heightfield = heightfield;
    }

    public override void _Ready()
    {
        _unitsPerMeter = SimConstants.GodotUnitsPerTile / SimConstants.MetersPerTile;

        var mesh = PylonsRenderer.LoadMergedMesh("res://assets/models/table_lamp.glb");
        if (mesh is null)
        {
            GD.PushError("TableLampRenderer: failed to load table_lamp.glb");
            return;
        }
        _bucket = new MultiMeshInstance3D
        {
            Name = "TableLamp",
            Multimesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                Mesh = mesh,
                InstanceCount = 0,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
        };
        AddChild(_bucket);
    }

    public override void _Process(double delta)
    {
        if (_bucket is null) return;

        var snap = _publisher.Current;
        var xforms = new List<Transform3D>();
        for (var i = 0; i < snap.Structures.Count; i++)
        {
            var s = snap.Structures[i];
            if (s.DefId != DefId) continue;
            if (!BlueprintCatalog.TryGet(s.DefId, out var def) || def is null) continue;

            var centerTileX = s.TileX + def.FootprintW * 0.5f;
            var centerTileY = s.TileY + def.FootprintH * 0.5f;
            var x = centerTileX * SimConstants.GodotUnitsPerTile;
            var z = centerTileY * SimConstants.GodotUnitsPerTile;
            var ground = SampleGround(centerTileX, centerTileY);
            var layerOffset = s.BaseLayer * LayerStepMeters * _unitsPerMeter;
            var baseY = ground + layerOffset;

            var scale = _unitsPerMeter;
            var basis = new Basis(Vector3.Up, 0f).Scaled(new Vector3(scale, scale, scale));
            xforms.Add(new Transform3D(basis, new Vector3(x, baseY, z)));
        }

        if (_bucket.Multimesh.InstanceCount != xforms.Count)
            _bucket.Multimesh.InstanceCount = xforms.Count;
        for (var i = 0; i < xforms.Count; i++)
            _bucket.Multimesh.SetInstanceTransform(i, xforms[i]);
    }

    private float SampleGround(float tileCenterX, float tileCenterY)
        => _heightfield.SurfaceMetresAt(tileCenterX, tileCenterY) * _unitsPerMeter;
}
