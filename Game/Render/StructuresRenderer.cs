using CowColonySim.Sim;
using CowColonySim.Sim.Blueprints;
using CowColonySim.Sim.Snapshots;
using CowColonySim.Sim.Terrain;
using CowColonySim.Sim.World.Components;
using Godot;

namespace CowColonySim.Game.Render;

// Reads SimSnapshot.Structures each frame and draws a solid box per built
// structure. Mirrors BlueprintGhostsRenderer but opaque + shaded so finished
// walls visibly differ from translucent ghosts.
public partial class StructuresRenderer : Node3D
{
    private const float LayerStepMeters = 0.75f;

    private SnapshotPublisher _publisher = null!;
    private Heightfield _heightfield = null!;
    private float _unitsPerMeter;
    private readonly Dictionary<int, MeshInstance3D> _boxes = new();
    private long _lastSig = -1;

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
        var sig = ComputeStructureSig(snap);
        if (sig == _lastSig) return;
        _lastSig = sig;

        var structures = snap.Structures;
        var seen = new HashSet<int>();

        for (var i = 0; i < structures.Count; i++)
        {
            var s = structures[i];
            if (!BlueprintCatalog.TryGet(s.DefId, out var def) || def is null) continue;
            // Pylons are drawn by PylonsRenderer (.glb meshes); skip them here so
            // the placeholder box doesn't render on top.
            if (def.Power == PowerNodeKind.Pylon) continue;
            // Table lamps are drawn by TableLampRenderer.
            if (s.DefId == "furniture.table_lamp") continue;
            seen.Add(s.EntityId);

            if (!_boxes.TryGetValue(s.EntityId, out var box))
            {
                box = MakeBox();
                _boxes[s.EntityId] = box;
                AddChild(box);
            }
            UpdateBox(box, s, def);
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
                    AlbedoColor = new Color(0.55f, 0.40f, 0.25f),
                },
            },
        };
    }

    private void UpdateBox(MeshInstance3D box, StructureView s, BlueprintDef def)
    {
        var (footW, footH) = RotatedFootprint(def.FootprintW, def.FootprintH, s.Rotation);
        var unitsPerTile = SimConstants.GodotUnitsPerTile;
        var sizeUnitsX = footW * unitsPerTile;
        var sizeUnitsZ = footH * unitsPerTile;
        var sizeUnitsY = def.HeightMeters * _unitsPerMeter;

        var mesh = (BoxMesh)box.Mesh;
        mesh.Size = new Vector3(sizeUnitsX, sizeUnitsY, sizeUnitsZ);

        var centerTileX = s.TileX + footW * 0.5f;
        var centerTileY = s.TileY + footH * 0.5f;
        var x = centerTileX * unitsPerTile;
        var z = centerTileY * unitsPerTile;

        var ground = SampleGround(centerTileX, centerTileY);
        var layerOffset = s.BaseLayer * LayerStepMeters * _unitsPerMeter;
        box.Position = new Vector3(x, ground + layerOffset + sizeUnitsY * 0.5f, z);
    }

    private static (int w, int h) RotatedFootprint(int w, int h, int rot)
        => (rot & 1) == 0 ? (w, h) : (h, w);

    private float SampleGround(float tileCenterX, float tileCenterY)
    {
        return _heightfield.SurfaceMetresAt(tileCenterX, tileCenterY) * _unitsPerMeter;
    }

    // Hash over the StructureViews this renderer actually draws (skipping
    // pylons + table lamps since other renderers own them). Frames where
    // nothing relevant changed early-return and skip the loop.
    private static long ComputeStructureSig(SimSnapshot snap)
    {
        unchecked
        {
            var h = 1469598103934665603L;
            var structs = snap.Structures;
            for (var i = 0; i < structs.Count; i++)
            {
                var s = structs[i];
                if (!BlueprintCatalog.TryGet(s.DefId, out var def) || def is null) continue;
                if (def.Power == PowerNodeKind.Pylon) continue;
                if (s.DefId == "furniture.table_lamp") continue;
                h = (h ^ s.EntityId) * 1099511628211L;
                h = (h ^ s.TileX) * 1099511628211L;
                h = (h ^ s.TileY) * 1099511628211L;
                h = (h ^ s.Rotation) * 1099511628211L;
                h = (h ^ s.BaseLayer) * 1099511628211L;
                h = (h ^ s.DefId.GetHashCode()) * 1099511628211L;
            }
            return h;
        }
    }
}
