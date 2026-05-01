using CowColonySim.Sim;
using CowColonySim.Sim.Snapshots;
using CowColonySim.Sim.Terrain;
using Godot;

namespace CowColonySim.Game.Selection;

// Yellow torus ring under selected entities. Pools child MeshInstance3Ds
// so multi-select draws one ring per colonist while still showing a ring
// for tree/boulder/item single-selects. Boulders use a wider mesh at the
// same y so the ring isn't hidden under the rock.
public partial class SelectionRing : Node3D
{
    private const float RingRadiusMeters = 0.6f;
    private const float RingThicknessMeters = 0.05f;
    private const float BoulderRingRadiusMeters = 1.1f;
    private const float BoulderRingThicknessMeters = 0.06f;

    private SelectionService _selection = null!;
    private SnapshotPublisher _publisher = null!;
    private Heightfield _heightfield = null!;
    private float _unitsPerMeter;
    private TorusMesh _defaultMesh = null!;
    private TorusMesh _boulderMesh = null!;
    private readonly List<MeshInstance3D> _pool = new();

    public void Configure(SelectionService selection, SnapshotPublisher publisher, Heightfield heightfield)
    {
        _selection = selection;
        _publisher = publisher;
        _heightfield = heightfield;
    }

    public override void _Ready()
    {
        _unitsPerMeter = SimConstants.GodotUnitsPerTile / SimConstants.MetersPerTile;

        var material = new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 0.85f, 0.2f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        _defaultMesh = new TorusMesh
        {
            InnerRadius = (RingRadiusMeters - RingThicknessMeters) * _unitsPerMeter,
            OuterRadius = RingRadiusMeters * _unitsPerMeter,
            RingSegments = 32,
            Material = material,
        };
        _boulderMesh = new TorusMesh
        {
            InnerRadius = (BoulderRingRadiusMeters - BoulderRingThicknessMeters) * _unitsPerMeter,
            OuterRadius = BoulderRingRadiusMeters * _unitsPerMeter,
            RingSegments = 32,
            Material = material,
        };
    }

    public override void _Process(double delta)
    {
        var snap = _publisher.Current;
        var used = 0;

        // Multi-select rings: one ring per colonist in the bulk set. Falls
        // back to SelectedEntityId when the bulk set is empty (single-pick).
        if (_selection.SelectedColonistIds.Count > 0)
        {
            foreach (var id in _selection.SelectedColonistIds)
            {
                if (TryPlaceColonistRing(snap, id, used)) used++;
            }
        }
        else if (_selection.SelectedEntityId is int selId)
        {
            if (TryPlaceColonistRing(snap, selId, used)) used++;
        }

        if (_selection.SelectedTreeId is int treeId)
        {
            if (TryPlaceTreeRing(snap, treeId, used)) used++;
        }
        if (_selection.SelectedBoulderId is int boulderId)
        {
            if (TryPlaceBoulderRing(snap, boulderId, used)) used++;
        }
        if (_selection.SelectedItemId is int itemId)
        {
            if (TryPlaceItemRing(snap, itemId, used)) used++;
        }

        for (var i = used; i < _pool.Count; i++) _pool[i].Visible = false;
    }

    private bool TryPlaceColonistRing(SimSnapshot snap, int id, int slot)
    {
        for (var i = 0; i < snap.Colonists.Count; i++)
        {
            var c = snap.Colonists[i];
            if (c.EntityId != id) continue;
            var x = c.MetersX * _unitsPerMeter;
            var z = c.MetersY * _unitsPerMeter;
            var y = SampleGroundUnits(c.MetersX, c.MetersY) + 1f;
            PlaceRing(slot, _defaultMesh, new Vector3(x, y, z));
            return true;
        }
        return false;
    }

    private bool TryPlaceTreeRing(SimSnapshot snap, int id, int slot)
    {
        for (var i = 0; i < snap.Trees.Count; i++)
        {
            var t = snap.Trees[i];
            if (t.EntityId != id) continue;
            PlaceTileCenterRing(slot, t.TileX, t.TileY, _defaultMesh);
            return true;
        }
        return false;
    }

    private bool TryPlaceBoulderRing(SimSnapshot snap, int id, int slot)
    {
        for (var i = 0; i < snap.Boulders.Count; i++)
        {
            var b = snap.Boulders[i];
            if (b.EntityId != id) continue;
            PlaceTileCenterRing(slot, b.TileX, b.TileY, _boulderMesh);
            return true;
        }
        return false;
    }

    private bool TryPlaceItemRing(SimSnapshot snap, int id, int slot)
    {
        for (var i = 0; i < snap.Items.Count; i++)
        {
            var it = snap.Items[i];
            if (it.EntityId != id) continue;
            PlaceTileCenterRing(slot, it.TileX, it.TileY, _defaultMesh);
            return true;
        }
        return false;
    }

    private void PlaceTileCenterRing(int slot, int tileX, int tileY, Mesh mesh)
    {
        var metersX = (tileX + 0.5f) * SimConstants.MetersPerTile;
        var metersY = (tileY + 0.5f) * SimConstants.MetersPerTile;
        var x = metersX * _unitsPerMeter;
        var z = metersY * _unitsPerMeter;
        var y = SampleGroundUnits(metersX, metersY) + 1f;
        PlaceRing(slot, mesh, new Vector3(x, y, z));
    }

    private void PlaceRing(int slot, Mesh mesh, Vector3 pos)
    {
        while (_pool.Count <= slot)
        {
            var mi = new MeshInstance3D
            {
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Visible = false,
            };
            AddChild(mi);
            _pool.Add(mi);
        }
        var ring = _pool[slot];
        ring.Mesh = mesh;
        ring.Position = pos;
        ring.Visible = true;
    }

    private float SampleGroundUnits(float metersX, float metersY)
    {
        var tilesX = metersX / SimConstants.MetersPerTile;
        var tilesY = metersY / SimConstants.MetersPerTile;
        return _heightfield.SurfaceMetresAt(tilesX, tilesY) * _unitsPerMeter;
    }
}
