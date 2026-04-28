using CowColonySim.Sim;
using CowColonySim.Sim.Snapshots;
using CowColonySim.Sim.Terrain;
using Godot;

namespace CowColonySim.Game.Render;

// Loads boulder.glb + stone[_2/_3].glb at runtime via GltfDocument so the
// renderer works without import sidecars (matches TreesRenderer pattern).
// Each mesh becomes one MultiMeshInstance3D bucket. BoulderView.Variant
// picks which bucket renders the entity. Per-instance rotation + scale
// jitter comes from VariantSeed so a boulder field doesn't look stamped.
public partial class BouldersRenderer : Node3D
{
    private static readonly string[] MeshPaths =
    {
        "res://assets/models/boulder.glb",
        "res://assets/models/stone.glb",
        "res://assets/models/stone_2.glb",
        "res://assets/models/stone_3.glb",
    };

    private SnapshotPublisher _publisher = null!;
    private Heightfield _heightfield = null!;
    private float _unitsPerMeter;
    private MultiMeshInstance3D[] _buckets = Array.Empty<MultiMeshInstance3D>();
    private float _time;

    public void Configure(SnapshotPublisher publisher, Heightfield heightfield)
    {
        _publisher = publisher;
        _heightfield = heightfield;
    }

    public override void _Ready()
    {
        _unitsPerMeter = SimConstants.GodotUnitsPerTile / SimConstants.MetersPerTile;

        var buckets = new List<MultiMeshInstance3D>(MeshPaths.Length);
        for (var v = 0; v < MeshPaths.Length; v++)
        {
            var mesh = LoadMergedMesh(MeshPaths[v]);
            if (mesh is null)
            {
                GD.PushWarning($"BouldersRenderer: skipping {MeshPaths[v]} (load failed)");
                buckets.Add(null!);
                continue;
            }
            var bucket = new MultiMeshInstance3D
            {
                Name = $"BoulderBucket{v}",
                Multimesh = new MultiMesh
                {
                    TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                    Mesh = mesh,
                    InstanceCount = 0,
                },
                CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
            };
            AddChild(bucket);
            buckets.Add(bucket);
        }
        _buckets = buckets.ToArray();
    }

    private static ArrayMesh? LoadMergedMesh(string resPath)
    {
        var absolute = ProjectSettings.GlobalizePath(resPath);
        if (!System.IO.File.Exists(absolute)) return null;
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
        if (_buckets.Length == 0) return;
        _time += (float)delta;
        var snap = _publisher.Current;
        var boulders = snap.Boulders;

        Span<int> counts = stackalloc int[_buckets.Length];
        for (var i = 0; i < boulders.Count; i++)
        {
            var v = ResolveVariant(boulders[i].Variant);
            counts[v]++;
        }
        for (var v = 0; v < _buckets.Length; v++)
        {
            if (_buckets[v] is null) continue;
            if (_buckets[v].Multimesh.InstanceCount != counts[v])
                _buckets[v].Multimesh.InstanceCount = counts[v];
        }

        Span<int> writeIdx = stackalloc int[_buckets.Length];
        for (var i = 0; i < boulders.Count; i++)
        {
            var b = boulders[i];
            var v = ResolveVariant(b.Variant);
            if (_buckets[v] is null) continue;

            var metersX = (b.TileX + 0.5f) * SimConstants.MetersPerTile;
            var metersY = (b.TileY + 0.5f) * SimConstants.MetersPerTile;
            var x = metersX * _unitsPerMeter;
            var z = metersY * _unitsPerMeter;
            var y = SampleGround(metersX, metersY);

            var seed = b.VariantSeed == 0 ? 0xB0B0B0B0u : b.VariantSeed;
            var angle = (seed % 3600u) * 0.1f * Mathf.Pi / 180f;
            // Boulder meshes authored in meters; world transforms run in units.
            // 0.85..1.20 scale jitter so a field reads varied without nesting.
            var jitter = 0.85f + ((seed >> 10) % 35u) / 100f;
            var scale = jitter * _unitsPerMeter;
            var basis = Basis.Identity
                .Rotated(Vector3.Up, angle)
                .Scaled(new Vector3(scale, scale, scale));

            // Wobble while a colonist is striking the boulder. Same shape as
            // Tree wobble but smaller — rocks don't sway as much as trunks.
            if (b.BeingMined)
            {
                var wobblePhase = _time * 14f + (seed & 0xFFu) * 0.024f;
                var tilt = MathF.Sin(wobblePhase) * 0.025f;
                var tiltAxis = new Vector3(MathF.Cos(seed & 0xFu), 0f, MathF.Sin(seed & 0xFu)).Normalized();
                basis = new Basis(tiltAxis, tilt) * basis;
            }

            var xform = new Transform3D(basis, new Vector3(x, y, z));
            _buckets[v].Multimesh.SetInstanceTransform(writeIdx[v]++, xform);
        }
    }

    private int ResolveVariant(int requested)
    {
        if (_buckets.Length == 0) return 0;
        var v = ((requested % _buckets.Length) + _buckets.Length) % _buckets.Length;
        // Fall back to first non-null bucket if the requested mesh failed to load.
        if (_buckets[v] is not null) return v;
        for (var i = 0; i < _buckets.Length; i++) if (_buckets[i] is not null) return i;
        return 0;
    }

    private float SampleGround(float metersX, float metersY)
    {
        var tilesX = metersX / SimConstants.MetersPerTile;
        var tilesY = metersY / SimConstants.MetersPerTile;
        return _heightfield.SurfaceMetresAt(tilesX, tilesY) * _unitsPerMeter;
    }
}
