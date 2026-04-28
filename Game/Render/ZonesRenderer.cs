using CowColonySim.Sim;
using CowColonySim.Sim.Snapshots;
using CowColonySim.Sim.Terrain;
using CowColonySim.Sim.Zones;
using Godot;

namespace CowColonySim.Game.Render;

// Reads SimSnapshot.Zones each frame and rebuilds a per-zone mesh that
// hugs the heightfield: two triangles per zone tile with vertices snapped
// to that tile's four corner heights. Mesh is cached per zone and only
// rebuilt when the zone rect or the heightfield revision changes, so a
// stable map costs almost nothing per frame. Stockpile = warm tan, Farm =
// green.
public partial class ZonesRenderer : Node3D
{
    private const float HoverUnits = 0.4f;

    private SnapshotPublisher _publisher = null!;
    private Heightfield _heightfield = null!;
    private float _unitsPerMeter;
    private readonly Dictionary<int, ZoneMesh> _meshes = new();

    private sealed class ZoneMesh
    {
        public MeshInstance3D Node = null!;
        public int CachedRectKey;
        public int CachedHeightVersion;
        public bool[]? CachedMask;
    }

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
            if (!_meshes.TryGetValue(z.ZoneId, out var entry))
            {
                entry = new ZoneMesh
                {
                    Node = new MeshInstance3D
                    {
                        MaterialOverride = MakeMaterial(z.Type),
                        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                    },
                    CachedRectKey = 0,
                    CachedHeightVersion = -1,
                };
                _meshes[z.ZoneId] = entry;
                AddChild(entry.Node);
            }

            var rectKey = RectKey(z);
            if (rectKey != entry.CachedRectKey
                || _heightfield.Version != entry.CachedHeightVersion
                || !ReferenceEquals(z.Mask, entry.CachedMask))
            {
                entry.Node.Mesh = BuildMesh(z);
                entry.CachedRectKey = rectKey;
                entry.CachedHeightVersion = _heightfield.Version;
                entry.CachedMask = z.Mask;
            }
        }

        if (_meshes.Count != seen.Count)
        {
            var stale = new List<int>();
            foreach (var kv in _meshes) if (!seen.Contains(kv.Key)) stale.Add(kv.Key);
            foreach (var id in stale)
            {
                _meshes[id].Node.QueueFree();
                _meshes.Remove(id);
            }
        }
    }

    private ArrayMesh BuildMesh(ZoneView z) =>
        TerrainStripMesh.Build(
            _heightfield, _unitsPerMeter,
            z.MinTileX, z.MinTileY, z.MaxTileX, z.MaxTileY,
            HoverUnits, z.Mask);

    // PerPixel shading instead of Unshaded so zones follow scene lighting —
    // they used to look like glow-in-the-dark patches at night because
    // Unshaded ignores ambient. A small Emission keeps a faint tint visible
    // even with the sun down so zones stay legible.
    private static StandardMaterial3D MakeMaterial(ZoneType type)
    {
        var color = ColorFor(type);
        return new StandardMaterial3D
        {
            AlbedoColor = color,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            EmissionEnabled = true,
            Emission = new Color(color.R, color.G, color.B),
            EmissionEnergyMultiplier = 0.12f,
        };
    }

    private static int RectKey(ZoneView z) =>
        HashCode.Combine(z.MinTileX, z.MinTileY, z.MaxTileX, z.MaxTileY);

    private static Color ColorFor(ZoneType type) => type switch
    {
        ZoneType.Stockpile => new Color(0.85f, 0.65f, 0.35f, 0.35f),
        ZoneType.Farm => new Color(0.35f, 0.75f, 0.30f, 0.35f),
        _ => new Color(0.6f, 0.6f, 0.6f, 0.35f),
    };
}
