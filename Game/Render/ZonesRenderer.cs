using CowColonySim.Sim;
using CowColonySim.Sim.Snapshots;
using CowColonySim.Sim.Terrain;
using CowColonySim.Sim.Zones;
using Godot;

namespace CowColonySim.Game.Render;

// Reads SimSnapshot.Zones each frame and rebuilds a translucent floor
// quad per zone. Quad sits a hair above the heightfield surface (worst-
// case slope sample at the rect corners) so it doesn't z-fight on hills.
// Stockpile = warm tan, Farm = green. Dummy renderer — full version
// will switch to a per-zone tile-strip mesh that hugs terrain exactly.
public partial class ZonesRenderer : Node3D
{
    private const float HoverUnits = 0.4f;

    private SnapshotPublisher _publisher = null!;
    private Heightfield _heightfield = null!;
    private float _unitsPerMeter;
    private readonly Dictionary<int, MeshInstance3D> _quads = new();

    public void Configure(SnapshotPublisher publisher, Heightfield heightfield)
    {
        _publisher = publisher;
        _heightfield = heightfield;
    }

    public override void _Ready()
    {
        _unitsPerMeter = SimConstants.GodotUnitsPerTile / SimConstants.MetersPerTile;
    }

    public override void _Process(double delta)
    {
        var snap = _publisher.Current;
        var zones = snap.Zones;
        var seen = new HashSet<int>();

        for (var i = 0; i < zones.Count; i++)
        {
            var z = zones[i];
            seen.Add(z.ZoneId);
            if (!_quads.TryGetValue(z.ZoneId, out var quad))
            {
                quad = MakeQuad(z.Type);
                _quads[z.ZoneId] = quad;
                AddChild(quad);
            }
            UpdateQuad(quad, z);
        }

        if (_quads.Count != seen.Count)
        {
            var stale = new List<int>();
            foreach (var kv in _quads) if (!seen.Contains(kv.Key)) stale.Add(kv.Key);
            foreach (var id in stale)
            {
                _quads[id].QueueFree();
                _quads.Remove(id);
            }
        }
    }

    private MeshInstance3D MakeQuad(ZoneType type)
    {
        var color = ColorFor(type);
        return new MeshInstance3D
        {
            Mesh = new PlaneMesh
            {
                Size = new Vector2(1f, 1f),
                Material = new StandardMaterial3D
                {
                    AlbedoColor = color,
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                },
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }

    private void UpdateQuad(MeshInstance3D quad, ZoneView z)
    {
        var unitsPerTile = SimConstants.GodotUnitsPerTile;
        var widthTiles = z.MaxTileX - z.MinTileX + 1;
        var heightTiles = z.MaxTileY - z.MinTileY + 1;
        var sizeUnitsX = widthTiles * unitsPerTile;
        var sizeUnitsZ = heightTiles * unitsPerTile;

        var plane = (PlaneMesh)quad.Mesh;
        plane.Size = new Vector2(sizeUnitsX, sizeUnitsZ);

        var centerTileX = (z.MinTileX + z.MaxTileX + 1) * 0.5f;
        var centerTileY = (z.MinTileY + z.MaxTileY + 1) * 0.5f;
        var x = centerTileX * unitsPerTile;
        var zPos = centerTileY * unitsPerTile;

        var maxH = SampleMaxCornerHeight(z);
        quad.Position = new Vector3(x, maxH + HoverUnits, zPos);
    }

    private float SampleMaxCornerHeight(ZoneView z)
    {
        var corners = new[]
        {
            (z.MinTileX, z.MinTileY),
            (z.MaxTileX + 1, z.MinTileY),
            (z.MinTileX, z.MaxTileY + 1),
            (z.MaxTileX + 1, z.MaxTileY + 1),
        };
        var max = float.NegativeInfinity;
        foreach (var (vx, vy) in corners)
        {
            var h = _heightfield.SurfaceMetresAt(vx, vy) * _unitsPerMeter;
            if (h > max) max = h;
        }
        return max;
    }

    private static Color ColorFor(ZoneType type) => type switch
    {
        ZoneType.Stockpile => new Color(0.85f, 0.65f, 0.35f, 0.35f),
        ZoneType.Farm => new Color(0.35f, 0.75f, 0.30f, 0.35f),
        _ => new Color(0.6f, 0.6f, 0.6f, 0.35f),
    };
}
