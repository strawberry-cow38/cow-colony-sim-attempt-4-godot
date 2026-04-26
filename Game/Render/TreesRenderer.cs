using CowColonySim.Sim;
using CowColonySim.Sim.Snapshots;
using CowColonySim.Sim.Terrain;
using Godot;

namespace CowColonySim.Game.Render;

// Loads pine.glb at runtime via GltfDocument so the renderer works
// without a .glb.import file (CLI build pipelines + freshly cloned
// machines never have those). Every MeshInstance3D under the imported
// scene is merged into one ArrayMesh so a single MultiMeshInstance3D
// can draw the whole forest in one batch — multimesh only renders one
// Mesh resource per bucket, so leaving trunk and canopy as siblings
// would only show one of them.
//
// Per-tree rotation + scale comes from TreeView.VariantSeed so the
// forest doesn't look stamped. Game side never touches Sim entities;
// it only reads the immutable snapshot.
public partial class TreesRenderer : Node3D
{
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

        var mesh = LoadMergedMesh("res://assets/models/pine.glb");
        if (mesh is null)
        {
            GD.PushError("TreesRenderer: failed to load pine.glb");
            return;
        }

        _bucket = new MultiMeshInstance3D
        {
            Name = "PineBucket",
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

    private static ArrayMesh? LoadMergedMesh(string resPath)
    {
        var absolute = ProjectSettings.GlobalizePath(resPath);
        if (!System.IO.File.Exists(absolute))
        {
            GD.PushWarning($"TreesRenderer: file not found at {absolute}");
            return null;
        }
        var doc = new GltfDocument();
        var state = new GltfState();
        var err = doc.AppendFromFile(absolute, state);
        if (err != Error.Ok) return null;
        var scene = doc.GenerateScene(state);
        if (scene is null) return null;

        var merged = new ArrayMesh();
        CollectInto(scene, Transform3D.Identity, merged);
        scene.QueueFree();
        return merged.GetSurfaceCount() > 0 ? merged : null;
    }

    private static void CollectInto(Node n, Transform3D parentXform, ArrayMesh into)
    {
        var xform = parentXform;
        if (n is Node3D n3d) xform = parentXform * n3d.Transform;
        if (n is MeshInstance3D mi && mi.Mesh is not null)
        {
            var src = mi.Mesh;
            for (var s = 0; s < src.GetSurfaceCount(); s++)
            {
                var arrays = src.SurfaceGetArrays(s);
                TransformPositions(arrays, xform);
                into.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
                var mat = mi.GetActiveMaterial(s) ?? src.SurfaceGetMaterial(s);
                if (mat is not null) into.SurfaceSetMaterial(into.GetSurfaceCount() - 1, mat);
            }
        }
        foreach (var child in n.GetChildren()) CollectInto(child, xform, into);
    }

    private static void TransformPositions(Godot.Collections.Array arrays, Transform3D xform)
    {
        if (arrays.Count <= (int)Mesh.ArrayType.Vertex) return;
        var positions = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        for (var i = 0; i < positions.Length; i++) positions[i] = xform * positions[i];
        arrays[(int)Mesh.ArrayType.Vertex] = positions;

        if (arrays.Count > (int)Mesh.ArrayType.Normal)
        {
            var normalsVar = arrays[(int)Mesh.ArrayType.Normal];
            if (normalsVar.VariantType == Variant.Type.PackedVector3Array)
            {
                var normals = normalsVar.AsVector3Array();
                var basis = xform.Basis.Inverse().Transposed();
                for (var i = 0; i < normals.Length; i++) normals[i] = (basis * normals[i]).Normalized();
                arrays[(int)Mesh.ArrayType.Normal] = normals;
            }
        }
    }

    public override void _Process(double delta)
    {
        if (_bucket is null) return;
        var snap = _publisher.Current;
        var trees = snap.Trees;

        if (_bucket.Multimesh.InstanceCount != trees.Count) _bucket.Multimesh.InstanceCount = trees.Count;

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
            // pine.glb is authored in meters; world transforms work in Godot
            // units (43u per 1.5m tile), so the mesh has to be scaled by
            // unitsPerMeter or the whole forest reads as a few cm tall.
            var jitter = 0.85f + ((seed >> 10) % 30u) / 100f;
            var scale = jitter * _unitsPerMeter;
            var basis = Basis.Identity
                .Rotated(Vector3.Up, angle)
                .Scaled(new Vector3(scale, scale, scale));
            var xform = new Transform3D(basis, new Vector3(x, y, z));
            _bucket.Multimesh.SetInstanceTransform(i, xform);
        }
    }

    private float SampleGround(float metersX, float metersY)
    {
        var tilesX = metersX / SimConstants.MetersPerTile;
        var tilesY = metersY / SimConstants.MetersPerTile;
        return _heightfield.SurfaceMetresAt(tilesX, tilesY) * _unitsPerMeter;
    }
}
