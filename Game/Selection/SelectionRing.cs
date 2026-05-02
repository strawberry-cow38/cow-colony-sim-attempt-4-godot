using CowColonySim.Game.Terrain;
using CowColonySim.Sim;
using CowColonySim.Sim.Snapshots;
using CowColonySim.Sim.Terrain;
using Godot;

namespace CowColonySim.Game.Selection;

// Yellow torus ring under selected entities. Pools child MeshInstance3Ds
// so multi-select draws one ring per entity. Ring scales per-entity from a
// single 1m base mesh — bigger things (large trees, multi-tile structures)
// get bigger rings so the ring isn't hidden inside the model.
public partial class SelectionRing : Node3D
{
    private const float ColonistRadiusMeters = 0.6f;
    private const float ItemRadiusMeters = 0.5f;
    private const float BoulderRadiusMeters = 1.1f;
    // Ring sticks out past the visible footprint by this much so the yellow
    // band is readable instead of hugging the model edge exactly.
    private const float FootprintMarginMeters = 0.25f;
    // Floor for tree rings — saplings/small trees still get a clickable
    // ring big enough to see.
    private const float TreeMinRadiusMeters = 0.55f;
    // Trees: pine.glb authored at ~1m base canopy radius. Empirical scale
    // factor that lines up the ring with the visible trunk + lower branches.
    private const float TreeRadiusFactor = 0.9f;

    private SelectionService _selection = null!;
    private SnapshotPublisher _publisher = null!;
    private Heightfield _heightfield = null!;
    private float _unitsPerMeter;
    private TorusMesh _baseMesh = null!;
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
        // Base torus is 1m outer radius, 5cm thick. Per-entity ring scales
        // this uniformly: scale = desired_radius_meters * unitsPerMeter.
        _baseMesh = new TorusMesh
        {
            InnerRadius = 0.95f,
            OuterRadius = 1.0f,
            RingSegments = 32,
            Material = material,
        };
    }

    public override void _Process(double delta)
    {
        var snap = _publisher.Current;
        var used = 0;

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

        if (_selection.SelectedTreeIds.Count > 0)
        {
            foreach (var id in _selection.SelectedTreeIds)
            {
                if (TryPlaceTreeRing(snap, id, used)) used++;
            }
        }
        else if (_selection.SelectedTreeId is int treeId)
        {
            if (TryPlaceTreeRing(snap, treeId, used)) used++;
        }

        if (_selection.SelectedBoulderIds.Count > 0)
        {
            foreach (var id in _selection.SelectedBoulderIds)
            {
                if (TryPlaceBoulderRing(snap, id, used)) used++;
            }
        }
        else if (_selection.SelectedBoulderId is int boulderId)
        {
            if (TryPlaceBoulderRing(snap, boulderId, used)) used++;
        }

        if (_selection.SelectedItemIds.Count > 0)
        {
            foreach (var id in _selection.SelectedItemIds)
            {
                if (TryPlaceItemRing(snap, id, used)) used++;
            }
        }
        else if (_selection.SelectedItemId is int itemId)
        {
            if (TryPlaceItemRing(snap, itemId, used)) used++;
        }

        if (_selection.SelectedBlueprintIds.Count > 0)
        {
            foreach (var id in _selection.SelectedBlueprintIds)
            {
                if (TryPlaceBlueprintRing(snap, id, used)) used++;
            }
        }
        else if (_selection.SelectedBlueprintId is int bpId)
        {
            if (TryPlaceBlueprintRing(snap, bpId, used)) used++;
        }

        if (_selection.SelectedStructureIds.Count > 0)
        {
            foreach (var id in _selection.SelectedStructureIds)
            {
                if (TryPlaceStructureRing(snap, id, used)) used++;
            }
        }
        else if (_selection.SelectedStructureId is int structId)
        {
            if (TryPlaceStructureRing(snap, structId, used)) used++;
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
            var topLookup = WalkableTopLookup.Build(snap);
            var ladderLookup = LadderTileLookup.Build(snap);
            var y = WalkableFloor.FeetUnits(_heightfield, _unitsPerMeter, c.MetersX, c.MetersY, c.MetersZ, topLookup, ladderLookup) + 1f;
            PlaceRing(slot, new Vector3(x, y, z), ColonistRadiusMeters);
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
            // Mirror TreesRenderer's per-tree visual scale (jitter * growth)
            // so the ring grows with the model. Floor at TreeMinRadiusMeters
            // so saplings stay clickable.
            var seed = t.VariantSeed == 0 ? 0xC0FFEE01u : t.VariantSeed;
            var jitter = 0.85f + ((seed >> 10) % 30u) / 100f;
            var growthScale = 0.15f + 0.85f * Math.Clamp(t.Growth, 0f, 100f) / 100f;
            var radius = Math.Max(TreeMinRadiusMeters, jitter * growthScale * TreeRadiusFactor);
            PlaceTileCenterRing(slot, t.TileX, t.TileY, radius);
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
            PlaceTileCenterRing(slot, b.TileX, b.TileY, BoulderRadiusMeters);
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
            PlaceTileCenterRing(slot, it.TileX, it.TileY, ItemRadiusMeters);
            return true;
        }
        return false;
    }

    private bool TryPlaceBlueprintRing(SimSnapshot snap, int id, int slot)
    {
        for (var i = 0; i < snap.BlueprintGhosts.Count; i++)
        {
            var g = snap.BlueprintGhosts[i];
            if (g.EntityId != id) continue;
            PlaceFootprintCenterRing(slot, g.OriginTileX, g.OriginTileY, g.DefId, g.Rotation);
            return true;
        }
        return false;
    }

    private bool TryPlaceStructureRing(SimSnapshot snap, int id, int slot)
    {
        for (var i = 0; i < snap.Structures.Count; i++)
        {
            var s = snap.Structures[i];
            if (s.EntityId != id) continue;
            PlaceFootprintCenterRing(slot, s.TileX, s.TileY, s.DefId, s.Rotation);
            return true;
        }
        return false;
    }

    private void PlaceFootprintCenterRing(int slot, int originTileX, int originTileY, string defId, int rotation)
    {
        var w = 1; var h = 1;
        if (Sim.Blueprints.BlueprintCatalog.TryGet(defId, out var def) && def is not null)
        {
            (w, h) = (rotation & 1) == 0 ? (def.FootprintW, def.FootprintH) : (def.FootprintH, def.FootprintW);
        }
        var metersX = (originTileX + w * 0.5f) * SimConstants.MetersPerTile;
        var metersY = (originTileY + h * 0.5f) * SimConstants.MetersPerTile;
        var x = metersX * _unitsPerMeter;
        var z = metersY * _unitsPerMeter;
        var y = SampleGroundUnits(metersX, metersY) + 1f;
        // Half-diagonal of the footprint in metres + a margin so the ring
        // clears even non-square structures (1x3 bench, 2x4 reactor etc).
        var halfW = w * 0.5f * SimConstants.MetersPerTile;
        var halfH = h * 0.5f * SimConstants.MetersPerTile;
        var radius = MathF.Sqrt(halfW * halfW + halfH * halfH) + FootprintMarginMeters;
        PlaceRing(slot, new Vector3(x, y, z), radius);
    }

    private void PlaceTileCenterRing(int slot, int tileX, int tileY, float radiusMeters)
    {
        var metersX = (tileX + 0.5f) * SimConstants.MetersPerTile;
        var metersY = (tileY + 0.5f) * SimConstants.MetersPerTile;
        var x = metersX * _unitsPerMeter;
        var z = metersY * _unitsPerMeter;
        var y = SampleGroundUnits(metersX, metersY) + 1f;
        PlaceRing(slot, new Vector3(x, y, z), radiusMeters);
    }

    private void PlaceRing(int slot, Vector3 pos, float radiusMeters)
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
        ring.Mesh = _baseMesh;
        ring.Position = pos;
        var s = radiusMeters * _unitsPerMeter;
        ring.Scale = new Vector3(s, s, s);
        ring.Visible = true;
    }

    private float SampleGroundUnits(float metersX, float metersY)
    {
        var tilesX = metersX / SimConstants.MetersPerTile;
        var tilesY = metersY / SimConstants.MetersPerTile;
        return _heightfield.SurfaceMetresAt(tilesX, tilesY) * _unitsPerMeter;
    }
}
