using CowColonySim.Sim;
using CowColonySim.Sim.Items;
using CowColonySim.Sim.Snapshots;
using CowColonySim.Sim.Terrain;
using Godot;

namespace CowColonySim.Game.Render;

// Renders ground item stacks. Each item kind has up to three tier
// meshes (wood.glb / wood_2.glb / wood_3.glb) chosen by Count/Capacity:
// tier = floor(min(1, count/cap) * 3) clamped to [0, 2]. Each tier gets
// its own MultiMeshInstance3D bucket so the GPU draws the whole forest
// pile in one batch per tier. Same runtime-glb load as TreesRenderer
// (no .glb.import required).
public partial class ItemsRenderer : Node3D
{
    private static readonly string[] WoodTierPaths =
    {
        "res://assets/models/wood.glb",
        "res://assets/models/wood_2.glb",
        "res://assets/models/wood_3.glb",
    };

    private SnapshotPublisher _publisher = null!;
    private Heightfield _heightfield = null!;
    private float _unitsPerMeter;
    private readonly MultiMeshInstance3D?[] _woodTiers = new MultiMeshInstance3D?[3];

    public void Configure(SnapshotPublisher publisher, Heightfield heightfield)
    {
        _publisher = publisher;
        _heightfield = heightfield;
    }

    public override void _Ready()
    {
        _unitsPerMeter = SimConstants.GodotUnitsPerTile / SimConstants.MetersPerTile;
        for (var i = 0; i < WoodTierPaths.Length; i++)
        {
            var mesh = LoadMergedMesh(WoodTierPaths[i]);
            if (mesh is null)
            {
                GD.PushWarning($"ItemsRenderer: failed to load {WoodTierPaths[i]}");
                continue;
            }
            var bucket = new MultiMeshInstance3D
            {
                Name = $"WoodTier{i}",
                Multimesh = new MultiMesh
                {
                    TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                    Mesh = mesh,
                    InstanceCount = 0,
                },
                CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
            };
            AddChild(bucket);
            _woodTiers[i] = bucket;
        }
    }

    private static int PileTier(int count, int capacity)
    {
        var cap = Math.Max(1, capacity);
        var frac = Math.Min(1f, (float)count / cap);
        var t = (int)MathF.Floor(frac * 3f);
        if (t > 2) t = 2;
        if (t < 0) t = 0;
        return t;
    }

    public override void _Process(double delta)
    {
        var snap = _publisher.Current;
        var items = snap.Items;

        var counts = new int[3];
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].Kind != ItemKind.Wood) continue;
            counts[PileTier(items[i].Count, items[i].Capacity)]++;
        }
        for (var t = 0; t < 3; t++)
        {
            var bucket = _woodTiers[t];
            if (bucket is null) continue;
            if (bucket.Multimesh.InstanceCount != counts[t]) bucket.Multimesh.InstanceCount = counts[t];
        }

        var idx = new int[3];
        var hoverUnits = 0.05f * _unitsPerMeter;
        for (var i = 0; i < items.Count; i++)
        {
            var it = items[i];
            if (it.Kind != ItemKind.Wood) continue;
            var tier = PileTier(it.Count, it.Capacity);
            var bucket = _woodTiers[tier];
            if (bucket is null) continue;

            var metersX = (it.TileX + 0.5f) * SimConstants.MetersPerTile;
            var metersY = (it.TileY + 0.5f) * SimConstants.MetersPerTile;
            var x = metersX * _unitsPerMeter;
            var z = metersY * _unitsPerMeter;
            var y = SampleGround(metersX, metersY) + hoverUnits;

            var seed = unchecked((uint)it.EntityId * 2654435761u);
            var angle = (seed % 3600u) * 0.1f * Mathf.Pi / 180f;
            var basis = Basis.Identity
                .Rotated(Vector3.Up, angle)
                .Scaled(new Vector3(_unitsPerMeter, _unitsPerMeter, _unitsPerMeter));
            var xform = new Transform3D(basis, new Vector3(x, y, z));
            bucket.Multimesh.SetInstanceTransform(idx[tier]++, xform);
        }
    }

    private float SampleGround(float metersX, float metersY)
    {
        var tilesX = metersX / SimConstants.MetersPerTile;
        var tilesY = metersY / SimConstants.MetersPerTile;
        return _heightfield.SurfaceMetresAt(tilesX, tilesY) * _unitsPerMeter;
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
}
