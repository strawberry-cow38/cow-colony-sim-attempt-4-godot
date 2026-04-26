using CowColonySim.Sim;
using CowColonySim.Sim.Blueprints;
using CowColonySim.Sim.Snapshots;
using CowColonySim.Sim.Terrain;
using Godot;

namespace CowColonySim.Game.Render;

// Reads SimSnapshot.BlueprintGhosts each frame and draws a translucent
// blue box per ghost sized to the def's footprint and HeightMeters.
// Rotation in 90° steps swaps W/H. BaseLayer offsets the box vertically
// so wall-tops can stack ghosts on z+1. Dummy renderer — real version
// will ghost the actual asset mesh.
public partial class BlueprintGhostsRenderer : Node3D
{
    private const float LayerStepMeters = 0.75f;
    private const float HoverUnits = 0.4f;

    private SnapshotPublisher _publisher = null!;
    private Heightfield _heightfield = null!;
    private float _unitsPerMeter;
    private readonly Dictionary<int, MeshInstance3D> _boxes = new();

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
        var ghosts = snap.BlueprintGhosts;
        var seen = new HashSet<int>();

        for (var i = 0; i < ghosts.Count; i++)
        {
            var g = ghosts[i];
            seen.Add(g.EntityId);
            if (!BlueprintCatalog.TryGet(g.DefId, out var def) || def is null) continue;

            if (!_boxes.TryGetValue(g.EntityId, out var box))
            {
                box = MakeBox();
                _boxes[g.EntityId] = box;
                AddChild(box);
            }
            UpdateBox(box, g, def);
        }

        if (_boxes.Count != seen.Count)
        {
            var stale = new List<int>();
            foreach (var kv in _boxes) if (!seen.Contains(kv.Key)) stale.Add(kv.Key);
            foreach (var id in stale) { _boxes[id].QueueFree(); _boxes.Remove(id); }
        }
    }

    private MeshInstance3D MakeBox()
    {
        return new MeshInstance3D
        {
            Mesh = new BoxMesh
            {
                Size = Vector3.One,
                Material = new StandardMaterial3D
                {
                    AlbedoColor = new Color(0.3f, 0.55f, 0.95f, 0.45f),
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                },
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }

    private void UpdateBox(MeshInstance3D box, BlueprintGhostView g, BlueprintDef def)
    {
        var (footW, footH) = RotatedFootprint(def.FootprintW, def.FootprintH, g.Rotation);
        var unitsPerTile = SimConstants.GodotUnitsPerTile;
        var sizeUnitsX = footW * unitsPerTile;
        var sizeUnitsZ = footH * unitsPerTile;
        var sizeUnitsY = def.HeightMeters * _unitsPerMeter;

        var mesh = (BoxMesh)box.Mesh;
        mesh.Size = new Vector3(sizeUnitsX, sizeUnitsY, sizeUnitsZ);

        var centerTileX = g.OriginTileX + footW * 0.5f;
        var centerTileY = g.OriginTileY + footH * 0.5f;
        var x = centerTileX * unitsPerTile;
        var z = centerTileY * unitsPerTile;

        var ground = SampleGround(centerTileX, centerTileY);
        var layerOffset = g.BaseLayer * LayerStepMeters * _unitsPerMeter;
        box.Position = new Vector3(x, ground + layerOffset + sizeUnitsY * 0.5f + HoverUnits, z);
    }

    private static (int w, int h) RotatedFootprint(int w, int h, int rot)
        => (rot & 1) == 0 ? (w, h) : (h, w);

    private float SampleGround(float tileCenterX, float tileCenterY)
    {
        return _heightfield.SurfaceMetresAt(tileCenterX, tileCenterY) * _unitsPerMeter;
    }
}
