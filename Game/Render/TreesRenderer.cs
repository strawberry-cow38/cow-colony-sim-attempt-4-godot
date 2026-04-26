using CowColonySim.Sim;
using CowColonySim.Sim.Snapshots;
using CowColonySim.Sim.Terrain;
using Godot;

namespace CowColonySim.Game.Render;

// One MultiMeshInstance3D per pine-mesh-surface, transforms fed each
// frame from snap.Trees so the GPU draws every tree in a single batch.
// pine.glb often has two surfaces (trunk + foliage with different
// materials), so we extract every MeshInstance3D inside the imported
// scene and bind them to parallel multi-mesh buckets — one bucket per
// surface, all sharing the same per-tree transform list.
//
// Per-tree rotation + scale is derived from TreeView.VariantSeed so the
// forest doesn't look stamped. Game side never touches Sim entities;
// it only reads the immutable snapshot.
public partial class TreesRenderer : Node3D
{
    private SnapshotPublisher _publisher = null!;
    private Heightfield _heightfield = null!;
    private float _unitsPerMeter;
    private readonly List<MultiMeshInstance3D> _buckets = new();

    public void Configure(SnapshotPublisher publisher, Heightfield heightfield)
    {
        _publisher = publisher;
        _heightfield = heightfield;
    }

    public override void _Ready()
    {
        _unitsPerMeter = SimConstants.GodotUnitsPerTile / SimConstants.MetersPerTile;

        var packed = ResourceLoader.Load<PackedScene>("res://assets/models/pine.glb");
        if (packed is null)
        {
            GD.PushError("TreesRenderer: pine.glb missing at res://assets/models/pine.glb");
            return;
        }
        var root = packed.Instantiate<Node3D>();
        AddMeshBucketsFrom(root);
        root.QueueFree();
    }

    private void AddMeshBucketsFrom(Node node)
    {
        if (node is MeshInstance3D mi && mi.Mesh is not null)
        {
            var mmi = new MultiMeshInstance3D
            {
                Name = $"PineBucket_{_buckets.Count}",
                Multimesh = new MultiMesh
                {
                    TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                    Mesh = mi.Mesh,
                    InstanceCount = 0,
                },
                CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
            };
            AddChild(mmi);
            _buckets.Add(mmi);
        }
        foreach (var child in node.GetChildren())
        {
            if (child is Node n) AddMeshBucketsFrom(n);
        }
    }

    public override void _Process(double delta)
    {
        if (_buckets.Count == 0) return;
        var snap = _publisher.Current;
        var trees = snap.Trees;

        foreach (var b in _buckets)
        {
            if (b.Multimesh.InstanceCount != trees.Count) b.Multimesh.InstanceCount = trees.Count;
        }

        for (var i = 0; i < trees.Count; i++)
        {
            var t = trees[i];
            var metersX = (t.TileX + 0.5f) * SimConstants.MetersPerTile;
            var metersY = (t.TileY + 0.5f) * SimConstants.MetersPerTile;
            var x = metersX * _unitsPerMeter;
            var z = metersY * _unitsPerMeter;
            var y = SampleGround(metersX, metersY);

            var seed = t.VariantSeed == 0 ? 0xC0FFEE01u : t.VariantSeed;
            var angle = (seed % 3600u) * 0.1f * Mathf.Pi / 180f;
            var scale = 0.85f + ((seed >> 10) % 30u) / 100f;
            var basis = Basis.Identity
                .Rotated(Vector3.Up, angle)
                .Scaled(new Vector3(scale, scale, scale));
            var xform = new Transform3D(basis, new Vector3(x, y, z));
            foreach (var b in _buckets) b.Multimesh.SetInstanceTransform(i, xform);
        }
    }

    private float SampleGround(float metersX, float metersY)
    {
        var tilesX = metersX / SimConstants.MetersPerTile;
        var tilesY = metersY / SimConstants.MetersPerTile;
        return _heightfield.SurfaceMetresAt(tilesX, tilesY) * _unitsPerMeter;
    }
}
