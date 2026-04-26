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
    private const float RingInnerMeters = 0.6f;
    private const float RingOuterMeters = 0.85f;

    private SnapshotPublisher _publisher = null!;
    private Heightfield _heightfield = null!;
    private float _unitsPerMeter;
    private float _unitsPerQuanta;

    private MeshInstance3D _lines = null!;
    private ImmediateMesh _linesMesh = null!;
    private MultiMeshInstance3D _rings = null!;

    public void Configure(SnapshotPublisher publisher, Heightfield heightfield)
    {
        _publisher = publisher;
        _heightfield = heightfield;
    }

    public override void _Ready()
    {
        _unitsPerMeter = SimConstants.GodotUnitsPerTile / SimConstants.MetersPerTile;
        _unitsPerQuanta = TerrainConstants.VerticalQuantumMetres * _unitsPerMeter;

        _linesMesh = new ImmediateMesh();
        var lineMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            AlbedoColor = new Color(1f, 0.95f, 0.4f),
            EmissionEnabled = true,
            Emission = new Color(1f, 0.95f, 0.4f),
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
        };
        AddChild(_lines);

        var torus = new TorusMesh
        {
            InnerRadius = RingInnerMeters * _unitsPerMeter,
            OuterRadius = RingOuterMeters * _unitsPerMeter,
            RingSegments = 24,
            Material = new StandardMaterial3D
            {
                AlbedoColor = new Color(1f, 0.85f, 0.25f),
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                EmissionEnabled = true,
                Emission = new Color(1f, 0.85f, 0.25f),
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
        };
        AddChild(_rings);
    }

    public override void _Process(double delta)
    {
        var snap = _publisher.Current;
        var paths = snap.Paths;

        _linesMesh.ClearSurfaces();
        if (_rings.Multimesh.InstanceCount != paths.Count)
        {
            _rings.Multimesh.InstanceCount = paths.Count;
        }

        var lift = LineLiftMeters * _unitsPerMeter;
        for (var p = 0; p < paths.Count; p++)
        {
            var view = paths[p];
            var tiles = view.RemainingTiles;
            if (tiles is null || tiles.Length == 0) continue;

            var colonistMeters = TryFindColonistMeters(snap, view.EntityId);

            _linesMesh.SurfaceBegin(Mesh.PrimitiveType.LineStrip);
            if (colonistMeters is { } cm)
            {
                _linesMesh.SurfaceAddVertex(MetersToWorld(cm.X, cm.Y, lift));
            }
            for (var i = 0; i < tiles.Length; i++)
            {
                var (mx, my) = TileCenter(tiles[i]);
                _linesMesh.SurfaceAddVertex(MetersToWorld(mx, my, lift));
            }
            _linesMesh.SurfaceEnd();

            var destTile = tiles[tiles.Length - 1];
            var (dmx, dmy) = TileCenter(destTile);
            var ringPos = MetersToWorld(dmx, dmy, lift);
            _rings.Multimesh.SetInstanceTransform(p, new Transform3D(Basis.Identity, ringPos));
        }
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

    // Today: nearest vertex from heightfield. When TileCoord becomes 3D
    // and PathView carries Z, this becomes coord.Z * VerticalQuantumMetres.
    private float SampleTileFloorY(float metersX, float metersY)
    {
        var tilesX = metersX / SimConstants.MetersPerTile;
        var tilesY = metersY / SimConstants.MetersPerTile;
        var vx = Mathf.Clamp((int)MathF.Round(tilesX), 0, _heightfield.VertWidth - 1);
        var vy = Mathf.Clamp((int)MathF.Round(tilesY), 0, _heightfield.VertHeight - 1);
        return _heightfield.Get(vx, vy) * _unitsPerQuanta;
    }
}
