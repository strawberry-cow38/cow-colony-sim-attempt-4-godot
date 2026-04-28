using CowColonySim.Sim;
using CowColonySim.Sim.Snapshots;
using CowColonySim.Sim.Terrain;
using Godot;

namespace CowColonySim.Game.Render;

// boulder.glb is one file holding many named sub-meshes — three rock
// shapes (boulder_a/b/c) each authored with mossy + non-mossy material
// variants (boulder_a_mossy, ...). The renderer extracts each named node
// into its own ArrayMesh so a single MultiMesh per variant can draw the
// whole population in one batch. BoulderView.Variant indexes into the
// VariantNames array (0..2 = clean shapes, 3..5 = mossy shapes).
public partial class BouldersRenderer : Node3D
{
    private const string SourceGlb = "res://assets/models/boulder.glb";
    private static readonly string[] VariantNames =
    {
        "boulder_a", "boulder_b", "boulder_c",
        "boulder_a_mossy", "boulder_b_mossy", "boulder_c_mossy",
    };

    private SnapshotPublisher _publisher = null!;
    private Heightfield _heightfield = null!;
    private float _unitsPerMeter;
    private MultiMeshInstance3D?[] _buckets = Array.Empty<MultiMeshInstance3D?>();
    private float _time;

    public void Configure(SnapshotPublisher publisher, Heightfield heightfield)
    {
        _publisher = publisher;
        _heightfield = heightfield;
    }

    public override void _Ready()
    {
        _unitsPerMeter = SimConstants.GodotUnitsPerTile / SimConstants.MetersPerTile;
        _buckets = new MultiMeshInstance3D?[VariantNames.Length];

        var scene = LoadGlbScene(SourceGlb);
        if (scene is null)
        {
            GD.PushError($"BouldersRenderer: failed to load {SourceGlb}");
            return;
        }
        for (var v = 0; v < VariantNames.Length; v++)
        {
            var mesh = ExtractNamedMesh(scene, VariantNames[v]);
            if (mesh is null)
            {
                GD.PushWarning($"BouldersRenderer: variant {VariantNames[v]} not found in glb");
                continue;
            }
            var bucket = new MultiMeshInstance3D
            {
                Name = $"BoulderBucket_{VariantNames[v]}",
                Multimesh = new MultiMesh
                {
                    TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                    Mesh = mesh,
                    InstanceCount = 0,
                },
                CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
            };
            AddChild(bucket);
            _buckets[v] = bucket;
        }
        scene.QueueFree();
    }

    private static Node? LoadGlbScene(string resPath)
    {
        var absolute = ProjectSettings.GlobalizePath(resPath);
        if (!System.IO.File.Exists(absolute)) return null;
        var doc = new GltfDocument();
        var state = new GltfState();
        var err = doc.AppendFromFile(absolute, state);
        if (err != Error.Ok) return null;
        return doc.GenerateScene(state);
    }

    // Find every MeshInstance3D under a node whose name matches `targetName`
    // (glTF node name from Blender) and merge their geometry into one
    // ArrayMesh. Match by Name.StartsWith — Godot can suffix duplicates
    // with @<n>, and Blender sometimes adds a primitive index.
    private static ArrayMesh? ExtractNamedMesh(Node root, string targetName)
    {
        var merged = new ArrayMesh();
        CollectByName(root, Transform3D.Identity, targetName, merged, ancestorMatch: false);
        if (merged.GetSurfaceCount() == 0) return null;
        // Each named submesh in boulder.glb is laid out at a different X/Z in the
        // source scene for Blender layout. Recenter X/Z so each variant sits at
        // origin and snaps onto its tile. Keep Y so the rock still sits on the
        // ground plane authored in Blender.
        var aabb = merged.GetAabb();
        var shift = new Vector3(-(aabb.Position.X + aabb.Size.X * 0.5f), 0f, -(aabb.Position.Z + aabb.Size.Z * 0.5f));
        if (shift.X == 0f && shift.Z == 0f) return merged;
        return RebuildShifted(merged, shift);
    }

    // ArrayMesh has no in-place vertex mutation, so we rebuild surface-by-
    // surface with shifted positions and return the new mesh. Materials are
    // copied across.
    private static ArrayMesh RebuildShifted(ArrayMesh src, Vector3 shift)
    {
        var rebuilt = new ArrayMesh();
        for (var s = 0; s < src.GetSurfaceCount(); s++)
        {
            var arrays = src.SurfaceGetArrays(s);
            if (arrays.Count > (int)Mesh.ArrayType.Vertex)
            {
                var positions = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                for (var i = 0; i < positions.Length; i++) positions[i] += shift;
                arrays[(int)Mesh.ArrayType.Vertex] = positions;
            }
            rebuilt.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            var mat = src.SurfaceGetMaterial(s);
            if (mat is not null) rebuilt.SurfaceSetMaterial(rebuilt.GetSurfaceCount() - 1, mat);
        }
        return rebuilt;
    }

    private static void CollectByName(Node n, Transform3D parentXform, string targetName, ArrayMesh into, bool ancestorMatch)
    {
        var xform = parentXform;
        if (n is Node3D n3d) xform = parentXform * n3d.Transform;

        var selfMatch = ancestorMatch || NameMatches(n.Name, targetName);

        if (selfMatch && n is MeshInstance3D mi && mi.Mesh is not null)
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

        foreach (var child in n.GetChildren()) CollectByName(child, xform, targetName, into, selfMatch);
    }

    private static bool NameMatches(string nodeName, string target)
    {
        if (nodeName == target) return true;
        // Godot dedupes with @ suffix (boulder_a@2). Blender splits multi-mat
        // primitives into "name", "name_001", etc. — both should match.
        if (nodeName.StartsWith(target + "@", StringComparison.Ordinal)) return true;
        if (nodeName.StartsWith(target + "_", StringComparison.Ordinal))
        {
            var tail = nodeName.AsSpan(target.Length + 1);
            for (var i = 0; i < tail.Length; i++) if (!char.IsDigit(tail[i])) return false;
            return tail.Length > 0;
        }
        return false;
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
            if (v >= 0) counts[v]++;
        }
        for (var v = 0; v < _buckets.Length; v++)
        {
            if (_buckets[v] is null) continue;
            if (_buckets[v]!.Multimesh.InstanceCount != counts[v])
                _buckets[v]!.Multimesh.InstanceCount = counts[v];
        }

        Span<int> writeIdx = stackalloc int[_buckets.Length];
        for (var i = 0; i < boulders.Count; i++)
        {
            var b = boulders[i];
            var v = ResolveVariant(b.Variant);
            if (v < 0) continue;

            var metersX = (b.TileX + 0.5f) * SimConstants.MetersPerTile;
            var metersY = (b.TileY + 0.5f) * SimConstants.MetersPerTile;
            var x = metersX * _unitsPerMeter;
            var z = metersY * _unitsPerMeter;
            var y = SampleGround(metersX, metersY);

            var seed = b.VariantSeed == 0 ? 0xB0B0B0B0u : b.VariantSeed;
            var angle = (seed % 3600u) * 0.1f * Mathf.Pi / 180f;
            var jitter = 0.85f + ((seed >> 10) % 35u) / 100f;
            var scale = jitter * _unitsPerMeter;
            var basis = Basis.Identity
                .Rotated(Vector3.Up, angle)
                .Scaled(new Vector3(scale, scale, scale));

            // Wobble while a colonist mines. Smaller amplitude than trees —
            // rocks shudder, they don't sway.
            if (b.BeingMined)
            {
                var wobblePhase = _time * 14f + (seed & 0xFFu) * 0.024f;
                var tilt = MathF.Sin(wobblePhase) * 0.025f;
                var tiltAxis = new Vector3(MathF.Cos(seed & 0xFu), 0f, MathF.Sin(seed & 0xFu)).Normalized();
                basis = new Basis(tiltAxis, tilt) * basis;
            }

            var xform = new Transform3D(basis, new Vector3(x, y, z));
            _buckets[v]!.Multimesh.SetInstanceTransform(writeIdx[v]++, xform);
        }
    }

    // Map an arbitrary Variant int onto a real loaded bucket. Falls back to
    // the first available bucket if the requested variant didn't load (so a
    // partial glb still draws every boulder somewhere).
    private int ResolveVariant(int requested)
    {
        if (_buckets.Length == 0) return -1;
        var v = ((requested % _buckets.Length) + _buckets.Length) % _buckets.Length;
        if (_buckets[v] is not null) return v;
        for (var i = 0; i < _buckets.Length; i++) if (_buckets[i] is not null) return i;
        return -1;
    }

    private float SampleGround(float metersX, float metersY)
    {
        var tilesX = metersX / SimConstants.MetersPerTile;
        var tilesY = metersY / SimConstants.MetersPerTile;
        return _heightfield.SurfaceMetresAt(tilesX, tilesY) * _unitsPerMeter;
    }
}
