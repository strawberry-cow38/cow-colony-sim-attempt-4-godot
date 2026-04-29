using CowColonySim.Sim;
using CowColonySim.Sim.Snapshots;
using CowColonySim.Sim.Terrain;
using CowColonySim.Sim.World.Components;
using Godot;

namespace CowColonySim.Game.Render;

// Reads PowerNodes + PowerEdges from SimSnapshot and renders:
//   * Sagging catenary cable for each PowerEdge — MultiMeshInstance3D with
//     a single shared cylinder mesh; per-instance transforms place segment
//     pieces along the curve. One instance buffer per snapshot frame.
//   * OmniLight3D under each Sink that has IsPowered. Lights toggle off
//     when the grid blackouts.
//
// Rebuilds when topology version (edge identity set) changes; otherwise
// just repositions lights from the snapshot.
public partial class PowerVisualsRenderer : Node3D
{
    private const int SegmentsPerEdge = 12;
    private const float SagPerMeter = 0.08f; // mid-span dip per metre of run
    private const float CableThicknessMeters = 0.08f;
    private const float PylonTopOffsetMeters = 4.0f; // where cable hangs off pylon
    private const float ConsumerTopOffsetMeters = 1.6f;
    private const float LayerStepMeters = 0.75f; // matches StructuresRenderer / build stack quantum

    private SnapshotPublisher _publisher = null!;
    private Heightfield _heightfield = null!;
    private float _unitsPerMeter;

    private MultiMeshInstance3D _cables = null!;
    private MultiMesh _multiMesh = null!;
    private readonly Dictionary<int, OmniLight3D> _sinkLights = new();
    private long _lastEdgeSig = -1;

    public void Configure(SnapshotPublisher publisher, Heightfield heightfield)
    {
        _publisher = publisher;
        _heightfield = heightfield;
    }

    public override void _Ready()
    {
        _unitsPerMeter = SimConstants.GodotUnitsPerTile / SimConstants.MetersPerTile;
        _multiMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = new CylinderMesh
            {
                TopRadius = CableThicknessMeters * _unitsPerMeter * 0.5f,
                BottomRadius = CableThicknessMeters * _unitsPerMeter * 0.5f,
                Height = 1f,
                RadialSegments = 5,
                Rings = 0,
                Material = new StandardMaterial3D
                {
                    AlbedoColor = new Color(0.07f, 0.07f, 0.07f),
                    Roughness = 0.7f,
                    Metallic = 0.1f,
                },
            },
            InstanceCount = 0,
        };
        _cables = new MultiMeshInstance3D { Multimesh = _multiMesh };
        AddChild(_cables);
    }

    public override void _Process(double delta)
    {
        var snap = _publisher.Current;
        UpdateCables(snap);
        UpdateLamps(snap);
    }

    private void UpdateCables(SimSnapshot snap)
    {
        var edges = snap.PowerEdges;
        var sig = ComputeEdgeSig(edges);
        if (sig == _lastEdgeSig) return;
        _lastEdgeSig = sig;

        var totalInstances = edges.Count * SegmentsPerEdge;
        _multiMesh.InstanceCount = totalInstances;
        if (totalInstances == 0) return;

        var unitsPerTile = SimConstants.GodotUnitsPerTile;
        var metersPerTile = SimConstants.MetersPerTile;
        for (var ei = 0; ei < edges.Count; ei++)
        {
            var e = edges[ei];
            var fromTopOffset = TopOffsetForNode(snap, e.FromEntityId);
            var toTopOffset = TopOffsetForNode(snap, e.ToEntityId);
            var fromX = (e.FromMetersX / metersPerTile) * unitsPerTile;
            var fromZ = (e.FromMetersY / metersPerTile) * unitsPerTile;
            var toX = (e.ToMetersX / metersPerTile) * unitsPerTile;
            var toZ = (e.ToMetersY / metersPerTile) * unitsPerTile;
            var fromGround = _heightfield.SurfaceMetresAt(e.FromMetersX / metersPerTile, e.FromMetersY / metersPerTile) * _unitsPerMeter;
            var toGround = _heightfield.SurfaceMetresAt(e.ToMetersX / metersPerTile, e.ToMetersY / metersPerTile) * _unitsPerMeter;
            var fromStack = e.FromBaseLayer * LayerStepMeters * _unitsPerMeter;
            var toStack = e.ToBaseLayer * LayerStepMeters * _unitsPerMeter;
            var fromY = fromGround + fromStack + fromTopOffset * _unitsPerMeter;
            var toY = toGround + toStack + toTopOffset * _unitsPerMeter;

            var dx = toX - fromX;
            var dz = toZ - fromZ;
            var horizMeters = MathF.Sqrt((dx * dx + dz * dz) / (_unitsPerMeter * _unitsPerMeter));
            var sagUnits = horizMeters * SagPerMeter * _unitsPerMeter;

            for (var s = 0; s < SegmentsPerEdge; s++)
            {
                var t0 = s / (float)SegmentsPerEdge;
                var t1 = (s + 1) / (float)SegmentsPerEdge;
                var p0 = SamplePoint(fromX, fromY, fromZ, toX, toY, toZ, sagUnits, t0);
                var p1 = SamplePoint(fromX, fromY, fromZ, toX, toY, toZ, sagUnits, t1);
                var xform = SegmentTransform(p0, p1);
                _multiMesh.SetInstanceTransform(ei * SegmentsPerEdge + s, xform);
            }
        }
    }

    private static Vector3 SamplePoint(float fromX, float fromY, float fromZ, float toX, float toY, float toZ, float sagUnits, float t)
    {
        var x = Mathf.Lerp(fromX, toX, t);
        var y = Mathf.Lerp(fromY, toY, t) - sagUnits * 4f * t * (1f - t);
        var z = Mathf.Lerp(fromZ, toZ, t);
        return new Vector3(x, y, z);
    }

    private static Transform3D SegmentTransform(Vector3 a, Vector3 b)
    {
        var diff = b - a;
        var len = diff.Length();
        if (len <= 0.0001f) return new Transform3D(Basis.Identity, a);
        var dir = diff / len;
        // Cylinder default axis is +Y. Rotate +Y to dir.
        var up = Vector3.Up;
        var dot = Mathf.Clamp(up.Dot(dir), -1f, 1f);
        Basis basis;
        if (dot > 0.9999f) basis = Basis.Identity;
        else if (dot < -0.9999f) basis = new Basis(new Vector3(1, 0, 0), Mathf.Pi);
        else
        {
            var axis = up.Cross(dir).Normalized();
            var angle = Mathf.Acos(dot);
            basis = new Basis(axis, angle);
        }
        basis = basis.Scaled(new Vector3(1f, len, 1f));
        var center = (a + b) * 0.5f;
        return new Transform3D(basis, center);
    }

    private float TopOffsetForNode(SimSnapshot snap, int entityId)
    {
        for (var i = 0; i < snap.PowerNodes.Count; i++)
        {
            if (snap.PowerNodes[i].EntityId != entityId) continue;
            return snap.PowerNodes[i].Kind == PowerNodeKind.Pylon ? PylonTopOffsetMeters : ConsumerTopOffsetMeters;
        }
        return PylonTopOffsetMeters;
    }

    private static long ComputeEdgeSig(IReadOnlyList<PowerEdgeView> edges)
    {
        unchecked
        {
            var h = 1469598103934665603L;
            for (var i = 0; i < edges.Count; i++)
            {
                var e = edges[i];
                h = (h ^ e.FromEntityId) * 1099511628211L;
                h = (h ^ e.ToEntityId) * 1099511628211L;
                h = (h ^ (e.IsHop ? 1 : 0)) * 1099511628211L;
            }
            return h;
        }
    }

    private void UpdateLamps(SimSnapshot snap)
    {
        var seen = new HashSet<int>();
        for (var i = 0; i < snap.PowerNodes.Count; i++)
        {
            var n = snap.PowerNodes[i];
            if (n.Kind != PowerNodeKind.Sink) continue;
            seen.Add(n.EntityId);
            if (!_sinkLights.TryGetValue(n.EntityId, out var light))
            {
                light = new OmniLight3D
                {
                    LightColor = new Color(1.0f, 0.92f, 0.65f),
                    LightEnergy = 6f,
                    OmniRange = 6f * SimConstants.GodotUnitsPerTile,
                    ShadowEnabled = false,
                };
                _sinkLights[n.EntityId] = light;
                AddChild(light);
            }
            var unitsPerTile = SimConstants.GodotUnitsPerTile;
            var metersPerTile = SimConstants.MetersPerTile;
            var x = (n.MetersX / metersPerTile) * unitsPerTile;
            var z = (n.MetersY / metersPerTile) * unitsPerTile;
            var ground = _heightfield.SurfaceMetresAt(n.MetersX / metersPerTile, n.MetersY / metersPerTile) * _unitsPerMeter;
            var stack = n.BaseLayer * LayerStepMeters * _unitsPerMeter;
            light.Position = new Vector3(x, ground + stack + ConsumerTopOffsetMeters * _unitsPerMeter, z);
            light.Visible = n.IsPowered;
        }
        if (_sinkLights.Count == seen.Count) return;
        var stale = new List<int>();
        foreach (var kv in _sinkLights) if (!seen.Contains(kv.Key)) stale.Add(kv.Key);
        foreach (var id in stale) { _sinkLights[id].QueueFree(); _sinkLights.Remove(id); }
    }
}
