using CowColonySim.Sim;
using CowColonySim.Sim.Blueprints;
using CowColonySim.Sim.Snapshots;
using CowColonySim.Sim.Terrain;
using CowColonySim.Sim.World.Components;
using Godot;

namespace CowColonySim.Game.Render;

// Reads SimSnapshot.BlueprintGhosts each frame and draws a translucent
// representation per ghost. Generic ghosts get a footprint-shaped box.
// Pylon ghosts swap in the real pylon.glb mesh, rotated to face their
// neighboring pylons (built or other ghosts) so a freshly placed run
// reads with the same line orientation it'll have once built.
public partial class BlueprintGhostsRenderer : Node3D
{
    private const float LayerStepMeters = 0.75f;
    private const float HoverUnits = 0.4f;

    private SnapshotPublisher _publisher = null!;
    private Heightfield _heightfield = null!;
    private float _unitsPerMeter;
    private ArrayMesh? _pylonMesh;
    private readonly Dictionary<int, MeshInstance3D> _instances = new();
    private readonly Dictionary<int, GhostKind> _instanceKind = new();

    private enum GhostKind { Box, Pylon }

    public void Configure(SnapshotPublisher publisher, Heightfield heightfield)
    {
        _publisher = publisher;
        _heightfield = heightfield;
    }

    public override void _Ready()
    {
        _unitsPerMeter = SimConstants.GodotUnitsPerTile / SimConstants.MetersPerTile;
        _pylonMesh = PylonsRenderer.LoadMergedMesh("res://assets/models/pylon.glb");
    }

    public override void _Process(double delta)
    {
        var snap = _publisher.Current;
        var ghosts = snap.BlueprintGhosts;
        var seen = new HashSet<int>();
        var pylonFacings = PylonsRenderer.BuildSyntheticPylonFacings(snap);

        for (var i = 0; i < ghosts.Count; i++)
        {
            var g = ghosts[i];
            seen.Add(g.EntityId);
            if (!BlueprintCatalog.TryGet(g.DefId, out var def) || def is null) continue;

            var kind = (def.Power == PowerNodeKind.Pylon && _pylonMesh is not null) ? GhostKind.Pylon : GhostKind.Box;
            if (!_instances.TryGetValue(g.EntityId, out var node) || _instanceKind[g.EntityId] != kind)
            {
                if (node is not null) { node.QueueFree(); }
                node = kind == GhostKind.Pylon ? MakePylonGhost() : MakeBox();
                _instances[g.EntityId] = node;
                _instanceKind[g.EntityId] = kind;
                AddChild(node);
            }
            if (kind == GhostKind.Pylon)
            {
                UpdatePylon(node, g, def, pylonFacings);
            }
            else
            {
                UpdateBox(node, g, def);
            }
        }

        if (_instances.Count != seen.Count)
        {
            var stale = new List<int>();
            foreach (var kv in _instances) if (!seen.Contains(kv.Key)) stale.Add(kv.Key);
            foreach (var id in stale)
            {
                _instances[id].QueueFree();
                _instances.Remove(id);
                _instanceKind.Remove(id);
            }
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

    private MeshInstance3D MakePylonGhost()
    {
        return new MeshInstance3D
        {
            Mesh = _pylonMesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.3f, 0.55f, 0.95f, 0.45f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            },
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

    private void UpdatePylon(MeshInstance3D node, BlueprintGhostView g, BlueprintDef def, Dictionary<int, Vector2> facings)
    {
        var centerTileX = g.OriginTileX + def.FootprintW * 0.5f;
        var centerTileY = g.OriginTileY + def.FootprintH * 0.5f;
        var x = centerTileX * SimConstants.GodotUnitsPerTile;
        var z = centerTileY * SimConstants.GodotUnitsPerTile;

        var ground = SampleGround(centerTileX, centerTileY);
        var layerOffset = g.BaseLayer * LayerStepMeters * _unitsPerMeter;
        var yaw = facings.TryGetValue(g.EntityId, out var dir) ? Mathf.Atan2(dir.X, dir.Y) : 0f;
        var scale = _unitsPerMeter;
        var basis = new Basis(Vector3.Up, yaw).Scaled(new Vector3(scale, scale, scale));
        node.Transform = new Transform3D(basis, new Vector3(x, ground + layerOffset, z));
    }

    private static (int w, int h) RotatedFootprint(int w, int h, int rot)
        => (rot & 1) == 0 ? (w, h) : (h, w);

    private float SampleGround(float tileCenterX, float tileCenterY)
    {
        return _heightfield.SurfaceMetresAt(tileCenterX, tileCenterY) * _unitsPerMeter;
    }
}
