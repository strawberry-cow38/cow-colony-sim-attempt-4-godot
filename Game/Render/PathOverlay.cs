using CowColonySim.Sim;
using CowColonySim.Sim.Pathfinding;
using CowColonySim.Sim.Snapshots;
using CowColonySim.Sim.Terrain;
using Godot;

namespace CowColonySim.Game.Render;

// Draws player-forced paths from SimSnapshot.Paths.
//   - One ImmediateMesh line strip per path, sampled at tile centres so
//     the line follows terrain slopes.
//   - One torus ring at the destination tile.
// Lifts the line a small offset above ground so it doesn't z-fight with
// the faceted terrain. Z-level ready: SampleTileFloorY today reads the
// heightfield by (x, y); when TileCoord gains Z, swap to coord.Z * quantum.
public partial class PathOverlay : Node3D
{
    private const float LineLiftMeters = 0.15f;
    // Torus tube radius = (Outer - Inner) / 2. The 0.6/0.85 pair gave a
    // 0.125m tube which vanishes at zoom-out — bumped to a 0.40m tube on
    // a 1.5m ring so waypoint markers stay legible at typical play
    // distances. Active + queued share the same dimensions.
    private const float RingInnerMeters = 1.1f;
    private const float RingOuterMeters = 1.5f;

    private SnapshotPublisher _publisher = null!;
    private Heightfield _heightfield = null!;
    private float _unitsPerMeter;

    private MeshInstance3D _lines = null!;
    private ImmediateMesh _linesMesh = null!;
    private MultiMeshInstance3D _rings = null!;

    public void Configure(SnapshotPublisher publisher, Heightfield heightfield)
    {
        _publisher = publisher;
        _heightfield = heightfield;
    }

    private static readonly Color ActiveColor = new(1f, 0.95f, 0.4f);
    private static readonly Color QueuedColor = new(0.35f, 0.75f, 1f);
    private MultiMeshInstance3D _queuedRings = null!;

    public override void _Ready()
    {
        _unitsPerMeter = SimConstants.GodotUnitsPerTile / SimConstants.MetersPerTile;

        _linesMesh = new ImmediateMesh();
        // VertexColorUseAsAlbedo lets us draw both active (yellow) and
        // queued (cyan) segments through a single ImmediateMesh — colour
        // is set per-vertex via SurfaceSetColor before each AddVertex.
        var lineMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            AlbedoColor = Colors.White,
            EmissionEnabled = true,
            Emission = Colors.White,
            EmissionEnergyMultiplier = 0.6f,
            DisableReceiveShadows = true,
            NoDepthTest = false,
        };
        _lines = new MeshInstance3D
        {
            Name = "PathLines",
            Mesh = _linesMesh,
            MaterialOverride = lineMat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            // ImmediateMesh recomputes its AABB from vertices each frame,
            // which made the path overlay get frustum-culled as soon as
            // the rig was anywhere outside the tight strip's bounds. A
            // huge cull margin keeps it drawn no matter where the camera
            // is — the geometry itself is tiny so cheap.
            ExtraCullMargin = 16384f,
        };
        AddChild(_lines);

        var torus = new TorusMesh
        {
            InnerRadius = RingInnerMeters * _unitsPerMeter,
            OuterRadius = RingOuterMeters * _unitsPerMeter,
            RingSegments = 24,
            Material = new StandardMaterial3D
            {
                AlbedoColor = ActiveColor,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                EmissionEnabled = true,
                Emission = ActiveColor,
                EmissionEnergyMultiplier = 0.7f,
            },
        };
        _rings = new MultiMeshInstance3D
        {
            Name = "DestRings",
            Multimesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                Mesh = torus,
                InstanceCount = 0,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            ExtraCullMargin = 16384f,
        };
        AddChild(_rings);

        var queuedTorus = new TorusMesh
        {
            InnerRadius = RingInnerMeters * _unitsPerMeter,
            OuterRadius = RingOuterMeters * _unitsPerMeter,
            RingSegments = 24,
            Material = new StandardMaterial3D
            {
                AlbedoColor = QueuedColor,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                EmissionEnabled = true,
                Emission = QueuedColor,
                EmissionEnergyMultiplier = 0.7f,
            },
        };
        _queuedRings = new MultiMeshInstance3D
        {
            Name = "QueuedRings",
            Multimesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                Mesh = queuedTorus,
                InstanceCount = 0,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            ExtraCullMargin = 16384f,
        };
        AddChild(_queuedRings);
    }

    public override void _Process(double delta)
    {
        var snap = _publisher.Current;
        var paths = snap.Paths;

        _linesMesh.ClearSurfaces();

        // Active rings: one per path with a real RemainingTiles strip.
        // Queued rings: one per QueuedTiles entry summed across all paths.
        var activeRingCount = 0;
        var queuedRingCount = 0;
        for (var p = 0; p < paths.Count; p++)
        {
            if (paths[p].RemainingTiles is { Length: > 0 }) activeRingCount++;
            queuedRingCount += paths[p].QueuedTiles?.Length ?? 0;
        }
        if (_rings.Multimesh.InstanceCount != activeRingCount) _rings.Multimesh.InstanceCount = activeRingCount;
        if (_queuedRings.Multimesh.InstanceCount != queuedRingCount) _queuedRings.Multimesh.InstanceCount = queuedRingCount;

        var lift = LineLiftMeters * _unitsPerMeter;
        var activeIdx = 0;
        var queuedIdx = 0;
        for (var p = 0; p < paths.Count; p++)
        {
            var view = paths[p];
            var tiles = view.RemainingTiles;
            var queued = view.QueuedTiles;
            var colonistMeters = TryFindColonistMeters(snap, view.EntityId);

            // Active strip: colonist → next waypoint → ... → leg dest.
            // Vector3 we end the active strip at also seeds the queued
            // strip so the line stays continuous across the join.
            Vector3? jointPos = null;
            if (tiles is { Length: > 0 })
            {
                _linesMesh.SurfaceBegin(Mesh.PrimitiveType.LineStrip);
                if (colonistMeters is { } cm)
                {
                    AddVertex(MetersToWorld(cm.X, cm.Y, lift), ActiveColor);
                }
                for (var i = 0; i < tiles.Length; i++)
                {
                    var (mx, my) = TileCenter(tiles[i]);
                    var v = MetersToWorld(mx, my, lift);
                    AddVertex(v, ActiveColor);
                    if (i == tiles.Length - 1) jointPos = v;
                }
                _linesMesh.SurfaceEnd();

                var (dmx, dmy) = TileCenter(tiles[tiles.Length - 1]);
                _rings.Multimesh.SetInstanceTransform(activeIdx++, new Transform3D(Basis.Identity, MetersToWorld(dmx, dmy, lift)));
            }
            else if (colonistMeters is { } cm)
            {
                // Active leg between planner requests — anchor the queued
                // strip at the colonist so it doesn't dangle in space.
                jointPos = MetersToWorld(cm.X, cm.Y, lift);
            }

            if (queued is null || queued.Length == 0) continue;

            _linesMesh.SurfaceBegin(Mesh.PrimitiveType.LineStrip);
            if (jointPos is Vector3 jp) AddVertex(jp, QueuedColor);
            for (var i = 0; i < queued.Length; i++)
            {
                var (qmx, qmy) = TileCenter(queued[i]);
                var qv = MetersToWorld(qmx, qmy, lift);
                AddVertex(qv, QueuedColor);
                _queuedRings.Multimesh.SetInstanceTransform(queuedIdx++, new Transform3D(Basis.Identity, qv));
            }
            _linesMesh.SurfaceEnd();
        }
    }

    private void AddVertex(Vector3 pos, Color color)
    {
        _linesMesh.SurfaceSetColor(color);
        _linesMesh.SurfaceAddVertex(pos);
    }

    private static (float X, float Y)? TryFindColonistMeters(SimSnapshot snap, int id)
    {
        for (var i = 0; i < snap.Colonists.Count; i++)
        {
            if (snap.Colonists[i].EntityId == id)
            {
                return (snap.Colonists[i].MetersX, snap.Colonists[i].MetersY);
            }
        }
        return null;
    }

    private static (float X, float Y) TileCenter(TileCoord t) =>
        ((t.X + 0.5f) * SimConstants.MetersPerTile,
         (t.Y + 0.5f) * SimConstants.MetersPerTile);

    private Vector3 MetersToWorld(float metersX, float metersY, float liftUnits)
    {
        var x = metersX * _unitsPerMeter;
        var z = metersY * _unitsPerMeter;
        var y = SampleTileFloorY(metersX, metersY) + liftUnits;
        return new Vector3(x, y, z);
    }

    // When TileCoord becomes 3D and PathView carries Z, this becomes
    // coord.Z * VerticalQuantumMetres instead of a surface sample.
    private float SampleTileFloorY(float metersX, float metersY)
    {
        var tilesX = metersX / SimConstants.MetersPerTile;
        var tilesY = metersY / SimConstants.MetersPerTile;
        return _heightfield.SurfaceMetresAt(tilesX, tilesY) * _unitsPerMeter;
    }
}
