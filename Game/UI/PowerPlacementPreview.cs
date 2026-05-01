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
    private MultiMeshInstance3D? _pylonGhostsIdeal;
    private MultiMeshInstance3D _cables = null!;
    private MultiMeshInstance3D _ranges = null!;
    private Func<BlueprintDef, Vector2I, (bool ok, int layer)>? _placementChecker;

    public void Configure(SnapshotPublisher publisher, Heightfield field)
    {
        _publisher = publisher;
        _field = field;
    }

    // PlacementTool wires this so the preview can mirror CommitSpacedDrag's
    // pullback. Without it, ideal == actual and no red "should be here"
    // ghost ever shows.
    public void SetPlacementChecker(PlacementCheckerFn checker) => _placementChecker = (d, p) =>
    {
        var ok = checker(d, p, out var layer);
        return (ok, layer);
    };

    public delegate bool PlacementCheckerFn(BlueprintDef def, Vector2I pt, out int layer);

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

            // Red "ideal" ghost — only populated for slots where the actual
            // pylon got pulled back to a different tile, so the player sees
            // both intent and outcome.
            _pylonGhostsIdeal = new MultiMeshInstance3D
            {
                Name = "PylonGhostsIdeal",
                Multimesh = new MultiMesh
                {
                    TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                    Mesh = pylonMesh,
                    InstanceCount = 0,
                },
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                MaterialOverride = new StandardMaterial3D
                {
                    AlbedoColor = new Color(1.0f, 0.30f, 0.30f, 0.40f),
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                },
            };
            AddChild(_pylonGhostsIdeal);
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
        if (_pylonGhostsIdeal is not null) _pylonGhostsIdeal.Multimesh.InstanceCount = 0;
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
        var slots = ComputeSpacedSlots(def, start, end);
        if (slots.Count == 0) { Hide(); return; }

        var actual = new List<Vector2I>(slots.Count);
        var idealOnly = new List<Vector2I>();
        for (var i = 0; i < slots.Count; i++)
        {
            actual.Add(slots[i].Actual);
            if (slots[i].Actual != slots[i].Ideal) idealOnly.Add(slots[i].Ideal);
        }

        Visible = true;
        var facings = ComputeDragFacings(actual);
        WritePylonGhosts(actual, facings);
        WriteIdealGhosts(idealOnly);
        WriteCables(actual);
        WriteRanges(actual);
    }

    // Replays the synthetic-facing fold over (existing pylons ∪ drag positions),
    // returning the unit facing for each drag-position index. Built/blueprint
    // pylons within hop range pull their drag-position neighbour's facing
    // toward them so the end ghost lines up with whatever it's snapping to.
    private Vector2[] ComputeDragFacings(List<Vector2I> positions)
    {
        var snap = _publisher.Current;
        var dragMx = new float[positions.Count];
        var dragMy = new float[positions.Count];
        for (var i = 0; i < positions.Count; i++)
        {
            dragMx[i] = (positions[i].X + 0.5f) * SimConstants.MetersPerTile;
            dragMy[i] = (positions[i].Y + 0.5f) * SimConstants.MetersPerTile;
        }

        var sums = new Vector2[positions.Count];

        void FoldPair(int i, float xJ, float yJ)
        {
            var dx = xJ - dragMx[i];
            var dz = yJ - dragMy[i];
            var sqr = dx * dx + dz * dz;
            var hopMeters = PowerSystem.CableHopTiles * SimConstants.MetersPerTile;
            if (sqr > hopMeters * hopMeters) return;
            var len = MathF.Sqrt(sqr);
            if (len < 0.001f) return;
            var ux = dx / len;
            var uz = dz / len;
            if (uz < 0f || (uz == 0f && ux < 0f)) { ux = -ux; uz = -uz; }
            sums[i] = new Vector2(sums[i].X + ux, sums[i].Y + uz);
        }

        // Drag-position to drag-position pairs (collect to both endpoints).
        for (var i = 0; i < positions.Count; i++)
        for (var j = i + 1; j < positions.Count; j++)
        {
            FoldPair(i, dragMx[j], dragMy[j]);
            FoldPair(j, dragMx[i], dragMy[i]);
        }
        // Drag-position to existing pylon (structures + non-drag blueprint ghosts).
        for (var s = 0; s < snap.Structures.Count; s++)
        {
            var st = snap.Structures[s];
            if (!BlueprintCatalog.TryGet(st.DefId, out var def) || def is null) continue;
            if (def.Power != PowerNodeKind.Pylon) continue;
            var px = (st.TileX + def.FootprintW * 0.5f) * SimConstants.MetersPerTile;
            var py = (st.TileY + def.FootprintH * 0.5f) * SimConstants.MetersPerTile;
            for (var i = 0; i < positions.Count; i++) FoldPair(i, px, py);
        }
        for (var b = 0; b < snap.BlueprintGhosts.Count; b++)
        {
            var g = snap.BlueprintGhosts[b];
            if (!BlueprintCatalog.TryGet(g.DefId, out var def) || def is null) continue;
            if (def.Power != PowerNodeKind.Pylon) continue;
            var px = (g.OriginTileX + def.FootprintW * 0.5f) * SimConstants.MetersPerTile;
            var py = (g.OriginTileY + def.FootprintH * 0.5f) * SimConstants.MetersPerTile;
            for (var i = 0; i < positions.Count; i++) FoldPair(i, px, py);
        }

        var result = new Vector2[positions.Count];
        for (var i = 0; i < positions.Count; i++)
        {
            var len = sums[i].Length();
            result[i] = len < 0.001f ? Vector2.Zero : sums[i] / len;
        }
        return result;
    }

    public readonly record struct PreviewSlot(Vector2I Ideal, Vector2I Actual);

    // Mirrors PlacementTool.CommitSpacedDrag end-to-end: ideal tile per
    // step + pullback search backward along the drag axis when the ideal
    // tile cannot host the def. When no checker is wired (e.g. unit
    // tests), Actual == Ideal for every slot.
    private List<PreviewSlot> ComputeSpacedSlots(BlueprintDef def, Vector2I start, Vector2I end)
    {
        var slots = new List<PreviewSlot>();
        var emitted = new HashSet<Vector2I>();
        Vector2I? lastPlaced = null;

        bool TryEmit(Vector2I ideal, Vector2I actual)
        {
            if (!emitted.Add(actual)) return false;
            slots.Add(new PreviewSlot(ideal, actual));
            lastPlaced = actual;
            return true;
        }

        var hopSqr = PowerSystem.CableHopTiles * PowerSystem.CableHopTiles;

        bool TryEmitWithPullback(Vector2I ideal, float nx, float ny, int maxPullback)
        {
            if (_placementChecker is null)
            {
                return TryEmit(ideal, ideal);
            }
            for (var back = 0; back <= maxPullback; back++)
            {
                var bx = ideal.X - nx * back;
                var by = ideal.Y - ny * back;
                var pt = new Vector2I((int)MathF.Round(bx), (int)MathF.Round(by));
                if (lastPlaced.HasValue && pt == lastPlaced.Value) return false;
                if (emitted.Contains(pt)) continue;
                // Mirror PlacementTool: skip candidates that would land out of
                // cable hop range from the previous pylon so the preview only
                // shows pylons that will actually wire up.
                if (lastPlaced.HasValue)
                {
                    var ddx = pt.X - lastPlaced.Value.X;
                    var ddy = pt.Y - lastPlaced.Value.Y;
                    if (ddx * ddx + ddy * ddy > hopSqr) continue;
                }
                var (ok, _) = _placementChecker(def, pt);
                if (!ok) continue;
                return TryEmit(ideal, pt);
            }
            return false;
        }

        // Start tile: try as-is, then pull back toward... nowhere (no axis
        // yet). PlacementTool just calls TryEmit(start) — we mirror that.
        if (_placementChecker is null)
        {
            TryEmit(start, start);
        }
        else
        {
            var (ok, _) = _placementChecker(def, start);
            if (ok) TryEmit(start, start);
        }

        var spacing = Math.Max(1, def.DragSpacingTiles);
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var dist = MathF.Sqrt(dx * dx + dy * dy);
        if (dist <= 0.0001f) return slots;
        var nrx = dx / dist;
        var nry = dy / dist;
        var steps = (int)MathF.Floor(dist / spacing);
        for (var i = 1; i <= steps; i++)
        {
            var px = start.X + nrx * (spacing * i);
            var py = start.Y + nry * (spacing * i);
            var ideal = new Vector2I((int)MathF.Round(px), (int)MathF.Round(py));
            TryEmitWithPullback(ideal, nrx, nry, spacing);
        }
        // Mirror CommitSpacedDrag: drop a ghost at the cursor itself so the
        // player sees the final pylon land where they're about to release.
        if (!lastPlaced.HasValue || end != lastPlaced.Value) TryEmitWithPullback(end, nrx, nry, spacing);
        return slots;
    }

    private void WritePylonGhosts(List<Vector2I> positions, Vector2[] facings)
    {
        if (_pylonGhosts is null) return;
        var mm = _pylonGhosts.Multimesh;
        mm.InstanceCount = positions.Count;
        var scale = _unitsPerMeter;
        for (var i = 0; i < positions.Count; i++)
        {
            var pos = positions[i];
            var (x, baseY, z) = ResolveAnchor(pos);
            var dir = facings[i];
            var yaw = dir == Vector2.Zero ? 0f : Mathf.Atan2(dir.X, dir.Y);
            var basis = new Basis(Vector3.Up, yaw).Scaled(new Vector3(scale, scale, scale));
            mm.SetInstanceTransform(i, new Transform3D(basis, new Vector3(x, baseY, z)));
        }
    }

    private void WriteIdealGhosts(List<Vector2I> idealOnly)
    {
        if (_pylonGhostsIdeal is null) return;
        var mm = _pylonGhostsIdeal.Multimesh;
        mm.InstanceCount = idealOnly.Count;
        var scale = _unitsPerMeter;
        for (var i = 0; i < idealOnly.Count; i++)
        {
            var (x, baseY, z) = ResolveAnchor(idealOnly[i]);
            var basis = Basis.Identity.Scaled(new Vector3(scale, scale, scale));
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
