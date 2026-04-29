using CowColonySim.Sim;
using CowColonySim.Sim.Blueprints;
using CowColonySim.Sim.Snapshots;
using CowColonySim.Sim.Terrain;
using CowColonySim.Sim.World.Components;
using Godot;

namespace CowColonySim.Game.Render;

// Renders all power pylons via .glb meshes in three multimesh buckets:
//   - default pylon (most common)
//   - electrical-box variant (random ~20% of regular pylons, deterministic by EntityId)
//   - lamp attachment (added on top of default, only for lamp pylons)
// StructuresRenderer skips pylons so it doesn't draw a placeholder box on top.
public partial class PylonsRenderer : Node3D
{
    private const float LayerStepMeters = 0.75f;
    // Vertical offset from pole base at which the lamp arm clamps to the pole.
    // Matches the .glb export origin: lamp_attachment.glb has Y=0 at the bracket
    // attachment height, so we just add this many metres to the pylon base Y.
    private const float LampArmHeightMeters = 3.0f;
    // 1-in-N regular pylons render the electrical-box variant. Deterministic
    // per-entity so it stays stable across saves and ticks.
    private const uint BoxVariantOneIn = 5;

    private SnapshotPublisher _publisher = null!;
    private Heightfield _heightfield = null!;
    private float _unitsPerMeter;
    private MultiMeshInstance3D? _defaultBucket;
    private MultiMeshInstance3D? _boxBucket;
    private MultiMeshInstance3D? _lampBucket;

    public void Configure(SnapshotPublisher publisher, Heightfield heightfield)
    {
        _publisher = publisher;
        _heightfield = heightfield;
    }

    public override void _Ready()
    {
        _unitsPerMeter = SimConstants.GodotUnitsPerTile / SimConstants.MetersPerTile;

        _defaultBucket = MakeBucket("PylonDefault", "res://assets/models/pylon.glb");
        _boxBucket = MakeBucket("PylonBox", "res://assets/models/pylon_with_box.glb");
        _lampBucket = MakeBucket("PylonLampAttach", "res://assets/models/pylon_lamp_attachment.glb");
    }

    private MultiMeshInstance3D? MakeBucket(string name, string resPath)
    {
        var mesh = LoadMergedMesh(resPath);
        if (mesh is null)
        {
            GD.PushError($"PylonsRenderer: failed to load {resPath}");
            return null;
        }
        var bucket = new MultiMeshInstance3D
        {
            Name = name,
            Multimesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                Mesh = mesh,
                InstanceCount = 0,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
        };
        AddChild(bucket);
        return bucket;
    }

    private static ArrayMesh? LoadMergedMesh(string resPath)
    {
        var absolute = ProjectSettings.GlobalizePath(resPath);
        if (!System.IO.File.Exists(absolute))
        {
            GD.PushWarning($"PylonsRenderer: file not found at {absolute}");
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
        if (_defaultBucket is null || _boxBucket is null || _lampBucket is null) return;

        var snap = _publisher.Current;
        var structures = snap.Structures;

        var defaultXforms = new System.Collections.Generic.List<Transform3D>();
        var boxXforms = new System.Collections.Generic.List<Transform3D>();
        var lampXforms = new System.Collections.Generic.List<Transform3D>();

        for (var i = 0; i < structures.Count; i++)
        {
            var s = structures[i];
            if (!BlueprintCatalog.TryGet(s.DefId, out var def) || def is null) continue;
            if (def.Power != PowerNodeKind.Pylon) continue;

            var centerTileX = s.TileX + def.FootprintW * 0.5f;
            var centerTileY = s.TileY + def.FootprintH * 0.5f;
            var x = centerTileX * SimConstants.GodotUnitsPerTile;
            var z = centerTileY * SimConstants.GodotUnitsPerTile;
            var ground = SampleGround(centerTileX, centerTileY);
            var layerOffset = s.BaseLayer * LayerStepMeters * _unitsPerMeter;
            var baseY = ground + layerOffset;

            var scale = _unitsPerMeter;
            var basis = Basis.Identity.Scaled(new Vector3(scale, scale, scale));
            var poleXform = new Transform3D(basis, new Vector3(x, baseY, z));

            var isLamp = def.DefaultDemandW > 0f;
            if (isLamp)
            {
                defaultXforms.Add(poleXform);
                var lampOffsetY = LampArmHeightMeters * _unitsPerMeter;
                var lampXform = new Transform3D(basis, new Vector3(x, baseY + lampOffsetY, z));
                lampXforms.Add(lampXform);
            }
            else if (IsBoxVariant(s.EntityId))
            {
                boxXforms.Add(poleXform);
            }
            else
            {
                defaultXforms.Add(poleXform);
            }
        }

        WriteBucket(_defaultBucket, defaultXforms);
        WriteBucket(_boxBucket, boxXforms);
        WriteBucket(_lampBucket, lampXforms);
    }

    private static bool IsBoxVariant(int entityId)
    {
        // Knuth multiplicative hash — cheap and well-distributed for sequential ids.
        var hash = unchecked((uint)entityId * 2654435761u);
        return (hash % BoxVariantOneIn) == 0;
    }

    private static void WriteBucket(MultiMeshInstance3D bucket, System.Collections.Generic.List<Transform3D> xforms)
    {
        if (bucket.Multimesh.InstanceCount != xforms.Count) bucket.Multimesh.InstanceCount = xforms.Count;
        for (var i = 0; i < xforms.Count; i++) bucket.Multimesh.SetInstanceTransform(i, xforms[i]);
    }

    private float SampleGround(float tileCenterX, float tileCenterY)
    {
        return _heightfield.SurfaceMetresAt(tileCenterX, tileCenterY) * _unitsPerMeter;
    }
}
