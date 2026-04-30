using CowColonySim.Game.Render;
using CowColonySim.Sim;
using CowColonySim.Sim.Blueprints;
using CowColonySim.Sim.Snapshots;
using CowColonySim.Sim.Systems;
using CowColonySim.Sim.Terrain;
using CowColonySim.Sim.World.Components;
using Godot;

namespace CowColonySim.Game.UI;

// Drag-time preview for SpacedDrag pylon placement. Walks the same interval
// math as PlacementTool.CommitSpacedDrag and renders:
//   * Translucent pylon ghost mesh at each interval (built from pylon.glb).
//   * Cable line between consecutive ghosts and snapping to the nearest
//     existing pylon (built or blueprint) within CableHopTiles.
//   * Translucent service-radius ring on the ground at each ghost
//     (ServiceRadiusTiles), so the player sees coverage while dragging.
public partial class PowerPlacementPreview : Node3D
{
    private const float LayerStepMeters = 0.75f;
    private const float PylonTopOffsetMeters = 3.85f;
    private const float CableThicknessMeters = 0.08f;
    private const float CableLateralOffsetMeters = 0.678f;
    private const int CableSegments = 8;
    private const float SagPerMeter = 0.08f;
    private const float RangeRingThicknessMeters = 0.18f;

    private SnapshotPublisher _publisher = null!;
    private Heightfield _field = null!;
    private float _unitsPerMeter;

    private MultiMeshInstance3D? _pylonGhosts;
    private MultiMeshInstance3D _cables = null!;
    private MultiMeshInstance3D _ranges = null!;

    public void Configure(SnapshotPublisher publisher, Heightfield field)
    {
        _publisher = publisher;
        _field = field;
    }

    public override void _Ready()
    {
        _unitsPerMeter = SimConstants.GodotUnitsPerTile / SimConstants.MetersPerTile;

        var pylonMesh = PylonsRenderer.LoadMergedMesh("res://assets/models/pylon.glb");
        if (pylonMesh is not null)
        {
            // MaterialOverride applies to all surfaces at the GeometryInstance3D
            // level — leaves the shared ArrayMesh's surface materials alone so
            // the real PylonsRenderer keeps its opaque pylon look.
            _pylonGhosts = new MultiMeshInstance3D
            {
                Name = "PylonGhosts",
                Multimesh = new MultiMesh
                {
                    TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                    Mesh = pylonMesh,
                    InstanceCount = 0,
                },
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                MaterialOverride = new StandardMaterial3D
                {
                    AlbedoColor = new Color(0.3f, 0.7f, 1.0f, 0.45f),
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                },
            };
            AddChild(_pylonGhosts);
        }

        var cableCyl = new CylinderMesh
        {
            TopRadius = CableThicknessMeters * _unitsPerMeter * 0.5f,
            BottomRadius = CableThicknessMeters * _unitsPerMeter * 0.5f,
            Height = 1f,
            RadialSegments = 5,
            Rings = 0,
            Material = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.1f, 0.7f, 1.0f, 0.7f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            },
        };
        _cables = new MultiMeshInstance3D
        {
            Name = "PreviewCables",
            Multimesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                Mesh = cableCyl,
                InstanceCount = 0,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_cables);

        var ringMesh = new TorusMesh
        {
            InnerRadius = (PowerSystem.ServiceRadiusTiles * SimConstants.MetersPerTile - RangeRingThicknessMeters) * _unitsPerMeter,
            OuterRadius = PowerSystem.ServiceRadiusTiles * SimConstants.MetersPerTile * _unitsPerMeter,
            RingSegments = 48,
            Material = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.3f, 0.95f, 0.6f, 0.45f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            },
        };
        _ranges = new MultiMeshInstance3D
        {
            Name = "PreviewRanges",
            Multimesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                Mesh = ringMesh,
                InstanceCount = 0,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_ranges);

        Visible = false;
    }

    public void HidePreview()
    {
        Visible = false;
        if (_pylonGhosts is not null) _pylonGhosts.Multimesh.InstanceCount = 0;
        _cables.Multimesh.InstanceCount = 0;
        _ranges.Multimesh.InstanceCount = 0;
    }

    // start: drag-down tile. end: cursor tile. Mirrors PlacementTool spacing.
    public void Show(BlueprintDef def, Vector2I start, Vector2I end)
    {
        if (def.Placement != PlacementMode.SpacedDrag || def.Power != PowerNodeKind.Pylon)
        {
            Hide();
            return;
        }
        var positions = ComputeSpacedPositions(def, start, end);
        if (positions.Count == 0) { Hide(); return; }

        Visible = true;
        WritePylonGhosts(positions);
        WriteCables(positions);
        WriteRanges(positions);
    }

    private List<Vector2I> ComputeSpacedPositions(BlueprintDef def, Vector2I start, Vector2I end)
    {
        // Same algorithm as PlacementTool.CommitSpacedDrag, minus the
        // command-bus emit. Validity isn't gated here: preview shows the
        // ideal positions even on uneven ground so the player sees intent.
        var spacing = Math.Max(1, def.DragSpacingTiles);
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var dist = MathF.Sqrt(dx * dx + dy * dy);
        var positions = new List<Vector2I> { start };
        if (dist <= 0.0001f) return positions;
        var nx = dx / dist;
        var ny = dy / dist;
        var steps = Math.Max(1, (int)MathF.Floor(dist / spacing));
        for (var i = 1; i <= steps; i++)
        {
            var px = start.X + nx * (spacing * i);
            var py = start.Y + ny * (spacing * i);
            var pt = new Vector2I((int)MathF.Round(px), (int)MathF.Round(py));
            if (positions.Count == 0 || positions[^1] != pt) positions.Add(pt);
        }
        if (positions[^1] != end) positions.Add(end);
        return positions;
    }

    private void WritePylonGhosts(List<Vector2I> positions)
    {
        if (_pylonGhosts is null) return;
        var mm = _pylonGhosts.Multimesh;
        mm.InstanceCount = positions.Count;
        var scale = _unitsPerMeter;
        var basis = Basis.Identity.Scaled(new Vector3(scale, scale, scale));
        for (var i = 0; i < positions.Count; i++)
        {
            var pos = positions[i];
            var (x, baseY, z) = ResolveAnchor(pos);
            mm.SetInstanceTransform(i, new Transform3D(basis, new Vector3(x, baseY, z)));
        }
    }

    private void WriteCables(List<Vector2I> positions)
    {
        var snap = _publisher.Current;

        // Endpoints to draw cables between: each interval position + snap-targets
        // on the start and end if there's an existing pylon within hop range.
        var startSnap = NearestExistingPylon(snap, positions[0]);
        var endSnap = positions.Count > 1 ? NearestExistingPylon(snap, positions[^1]) : null;

        var endpointsCount = positions.Count;
        if (startSnap is not null) endpointsCount++;
        if (endSnap is not null) endpointsCount++;
        var spans = endpointsCount - 1;
        if (spans <= 0)
        {
            _cables.Multimesh.InstanceCount = 0;
            return;
        }

        var anchors = new (float x, float y, float z)[endpointsCount];
        var idx = 0;
        if (startSnap is not null) anchors[idx++] = startSnap.Value;
        for (var i = 0; i < positions.Count; i++)
        {
            var (x, baseY, z) = ResolveAnchor(positions[i]);
            var topY = baseY + PylonTopOffsetMeters * _unitsPerMeter;
            anchors[idx++] = (x, topY, z);
        }
        if (endSnap is not null) anchors[idx++] = endSnap.Value;

        var totalInstances = spans * CableSegments * 2;
        var mm = _cables.Multimesh;
        mm.InstanceCount = totalInstances;

        var insulatorUnits = CableLateralOffsetMeters * _unitsPerMeter;
        var instance = 0;
        for (var s = 0; s < spans; s++)
        {
            var a = anchors[s];
            var b = anchors[s + 1];
            var dx = b.x - a.x;
            var dz = b.z - a.z;
            var horizUnits = MathF.Sqrt(dx * dx + dz * dz);
            var horizMeters = horizUnits / _unitsPerMeter;
            var sagUnits = horizMeters * SagPerMeter * _unitsPerMeter;
            var perpX = horizUnits < 0.0001f ? 0f : -dz / horizUnits * insulatorUnits;
            var perpZ = horizUnits < 0.0001f ? 0f : dx / horizUnits * insulatorUnits;
            for (var c = 0; c < 2; c++)
            {
                var sign = c == 0 ? -1f : 1f;
                var aX = a.x + perpX * sign;
                var aZ = a.z + perpZ * sign;
                var bX = b.x + perpX * sign;
                var bZ = b.z + perpZ * sign;
                for (var seg = 0; seg < CableSegments; seg++)
                {
                    var t0 = seg / (float)CableSegments;
                    var t1 = (seg + 1) / (float)CableSegments;
                    var p0 = SampleCatenary(aX, a.y, aZ, bX, b.y, bZ, sagUnits, t0);
                    var p1 = SampleCatenary(aX, a.y, aZ, bX, b.y, bZ, sagUnits, t1);
                    mm.SetInstanceTransform(instance++, SegmentTransform(p0, p1));
                }
            }
        }
    }

    private void WriteRanges(List<Vector2I> positions)
    {
        var mm = _ranges.Multimesh;
        mm.InstanceCount = positions.Count;
        for (var i = 0; i < positions.Count; i++)
        {
            var (x, baseY, z) = ResolveAnchor(positions[i]);
            mm.SetInstanceTransform(i, new Transform3D(Basis.Identity, new Vector3(x, baseY + 0.6f, z)));
        }
    }

    private (float x, float baseY, float z) ResolveAnchor(Vector2I tile)
    {
        var centerX = tile.X + 0.5f;
        var centerY = tile.Y + 0.5f;
        var x = centerX * SimConstants.GodotUnitsPerTile;
        var z = centerY * SimConstants.GodotUnitsPerTile;
        var ground = _field.SurfaceMetresAt(centerX, centerY) * _unitsPerMeter;
        return (x, ground, z);
    }

    private (float x, float y, float z)? NearestExistingPylon(SimSnapshot snap, Vector2I from)
    {
        var hopMeters = PowerSystem.CableHopTiles * SimConstants.MetersPerTile;
        var hopUnits = hopMeters * _unitsPerMeter;
        var fromX = (from.X + 0.5f) * SimConstants.GodotUnitsPerTile;
        var fromZ = (from.Y + 0.5f) * SimConstants.GodotUnitsPerTile;
        float bestSqr = hopUnits * hopUnits;
        (float, float, float)? best = null;

        for (var i = 0; i < snap.Structures.Count; i++)
        {
            var s = snap.Structures[i];
            if (!BlueprintCatalog.TryGet(s.DefId, out var def) || def is null) continue;
            if (def.Power != PowerNodeKind.Pylon) continue;
            var cx = (s.TileX + def.FootprintW * 0.5f) * SimConstants.GodotUnitsPerTile;
            var cz = (s.TileY + def.FootprintH * 0.5f) * SimConstants.GodotUnitsPerTile;
            var dx = cx - fromX; var dz = cz - fromZ;
            var sqr = dx * dx + dz * dz;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                var ground = _field.SurfaceMetresAt(s.TileX + def.FootprintW * 0.5f, s.TileY + def.FootprintH * 0.5f) * _unitsPerMeter;
                var topY = ground + s.BaseLayer * LayerStepMeters * _unitsPerMeter + PylonTopOffsetMeters * _unitsPerMeter;
                best = (cx, topY, cz);
            }
        }
        for (var i = 0; i < snap.BlueprintGhosts.Count; i++)
        {
            var g = snap.BlueprintGhosts[i];
            if (!BlueprintCatalog.TryGet(g.DefId, out var def) || def is null) continue;
            if (def.Power != PowerNodeKind.Pylon) continue;
            var cx = (g.OriginTileX + def.FootprintW * 0.5f) * SimConstants.GodotUnitsPerTile;
            var cz = (g.OriginTileY + def.FootprintH * 0.5f) * SimConstants.GodotUnitsPerTile;
            var dx = cx - fromX; var dz = cz - fromZ;
            var sqr = dx * dx + dz * dz;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                var ground = _field.SurfaceMetresAt(g.OriginTileX + def.FootprintW * 0.5f, g.OriginTileY + def.FootprintH * 0.5f) * _unitsPerMeter;
                var topY = ground + g.BaseLayer * LayerStepMeters * _unitsPerMeter + PylonTopOffsetMeters * _unitsPerMeter;
                best = (cx, topY, cz);
            }
        }
        return best;
    }

    private static Vector3 SampleCatenary(float fromX, float fromY, float fromZ, float toX, float toY, float toZ, float sagUnits, float t)
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
        var ortho = MathF.Abs(dir.Y) < 0.99f ? Vector3.Up : Vector3.Right;
        var xAxis = dir.Cross(ortho).Normalized();
        var zAxis = xAxis.Cross(dir).Normalized();
        var yAxis = dir * len;
        var basis = Basis.Identity;
        basis.X = xAxis;
        basis.Y = yAxis;
        basis.Z = zAxis;
        var center = (a + b) * 0.5f;
        return new Transform3D(basis, center);
    }

}
