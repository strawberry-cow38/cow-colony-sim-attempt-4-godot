using CowColonySim.Game.Terrain;
using CowColonySim.Game.UI;
using CowColonySim.Sim;
using CowColonySim.Sim.Commands;
using CowColonySim.Sim.Designations;
using CowColonySim.Sim.Logging;
using CowColonySim.Sim.Pathfinding;
using CowColonySim.Sim.Snapshots;
using CowColonySim.Sim.Terrain;
using CowColonySim.Sim.Zones;
using Godot;

namespace CowColonySim.Game.Selection;

// Click-to-select + right-click-to-move. Reads the latest snapshot to
// pick the colonist nearest the mouse ray; pushes MoveCommand back to
// Sim through the CommandBus. Selection state is Game-side only.
public partial class SelectionService : Node
{
    private const float ColonistPickRadiusUnits = 30f;
    private const int TilesPerCell = 256;

    private SnapshotPublisher _publisher = null!;
    private CommandBus _commands = null!;
    private Heightfield _heightfield = null!;
    private BuildToolService? _tools;
    private ContextMenu? _contextMenu;
    private float _unitsPerMeter;

    public int? SelectedEntityId { get; private set; }
    public int? SelectedZoneId { get; private set; }
    public int? SelectedTreeId { get; private set; }
    public int? SelectedBoulderId { get; private set; }
    public int? SelectedItemId { get; private set; }
    public int? SelectedBlueprintId { get; private set; }
    public int? SelectedStructureId { get; private set; }
    public Vector2? SelectedGroundXZUnits { get; private set; }

    public event System.Action? SelectionChanged;

    public void Configure(SnapshotPublisher publisher, CommandBus commands, Heightfield heightfield)
    {
        _publisher = publisher;
        _commands = commands;
        _heightfield = heightfield;
    }

    public void SetBuildTools(BuildToolService tools) => _tools = tools;

    public void SetContextMenu(ContextMenu menu) => _contextMenu = menu;

    public override void _Ready()
    {
        _unitsPerMeter = SimConstants.GodotUnitsPerTile / SimConstants.MetersPerTile;
    }

    public override void _Process(double delta)
    {
        DropStaleTreeSelection();
        DropStaleBoulderSelection();
        DropStaleItemSelection();
        DropStaleBlueprintSelection();
        DropStaleStructureSelection();
    }

    private void DropStaleBlueprintSelection()
    {
        if (SelectedBlueprintId is not int id) return;
        var ghosts = _publisher.Current.BlueprintGhosts;
        for (var i = 0; i < ghosts.Count; i++)
        {
            if (ghosts[i].EntityId == id) return;
        }
        SelectedBlueprintId = null;
        _contextMenu?.CloseIfOpen();
        SelectionChanged?.Invoke();
    }

    private void DropStaleStructureSelection()
    {
        if (SelectedStructureId is not int id) return;
        var structures = _publisher.Current.Structures;
        for (var i = 0; i < structures.Count; i++)
        {
            if (structures[i].EntityId == id) return;
        }
        SelectedStructureId = null;
        _contextMenu?.CloseIfOpen();
        SelectionChanged?.Invoke();
    }

    // When a tree is felled (or an item is consumed) the snapshot stops
    // including its EntityId, so the InfoPanel + context menu would otherwise
    // hang on a dead reference. Clear the selection one tick after the entity
    // disappears so panels close the moment it falls.
    private void DropStaleTreeSelection()
    {
        if (SelectedTreeId is not int id) return;
        var trees = _publisher.Current.Trees;
        for (var i = 0; i < trees.Count; i++)
        {
            if (trees[i].EntityId == id) return;
        }
        SelectedTreeId = null;
        _contextMenu?.CloseIfOpen();
        SelectionChanged?.Invoke();
    }

    private void DropStaleBoulderSelection()
    {
        if (SelectedBoulderId is not int id) return;
        var boulders = _publisher.Current.Boulders;
        for (var i = 0; i < boulders.Count; i++)
        {
            if (boulders[i].EntityId == id) return;
        }
        SelectedBoulderId = null;
        _contextMenu?.CloseIfOpen();
        SelectionChanged?.Invoke();
    }

    // Keyboard shortcuts that act on the currently-selected entity.
    //   F  = toggle forbid (items)
    //   X  = "do the work" — designate chop on a tree, mine on a boulder,
    //        deconstruct a structure, delete a zone
    //   C  = cancel — erase any designation tagged on the selected target
    // We dispatch by which Selected*Id is set; if nothing is selected, the
    // key falls through to the rest of the unhandled-input chain.
    private void HandleSelectionShortcut(InputEventKey ke)
    {
        switch (ke.PhysicalKeycode)
        {
            case Key.F:
                ApplyForbidShortcut();
                break;
            case Key.X:
                ApplyWorkShortcut();
                break;
            case Key.C:
                ApplyCancelShortcut();
                break;
        }
    }

    private void ApplyForbidShortcut()
    {
        if (SelectedItemId is not int id) return;
        var snap = _publisher.Current;
        for (var i = 0; i < snap.Items.Count; i++)
        {
            var it = snap.Items[i];
            if (it.EntityId != id) continue;
            _commands.Submit(new SetItemForbiddenCommand(id, !it.Forbidden));
            return;
        }
    }

    private void ApplyWorkShortcut()
    {
        var snap = _publisher.Current;
        if (SelectedTreeId is int treeId)
        {
            for (var i = 0; i < snap.Trees.Count; i++)
            {
                var t = snap.Trees[i];
                if (t.EntityId != treeId) continue;
                _commands.Submit(new StampDesignationsCommand(
                    DesignationKind.ChopTree,
                    new TileRect(t.TileX, t.TileY, t.TileX, t.TileY)));
                return;
            }
            return;
        }
        if (SelectedBoulderId is int boulderId)
        {
            for (var i = 0; i < snap.Boulders.Count; i++)
            {
                var b = snap.Boulders[i];
                if (b.EntityId != boulderId) continue;
                _commands.Submit(new StampDesignationsCommand(
                    DesignationKind.Mine,
                    new TileRect(b.TileX, b.TileY, b.TileX, b.TileY)));
                return;
            }
            return;
        }
        if (SelectedStructureId is int structId)
        {
            _commands.Submit(new DeconstructStructureCommand(structId));
            return;
        }
        if (SelectedZoneId is int zoneId)
        {
            for (var i = 0; i < snap.Zones.Count; i++)
            {
                var z = snap.Zones[i];
                if (z.ZoneId != zoneId) continue;
                _commands.Submit(new EraseInRectCommand(
                    new TileRect(z.MinTileX, z.MinTileY, z.MaxTileX, z.MaxTileY)));
                return;
            }
        }
    }

    private void ApplyCancelShortcut()
    {
        var snap = _publisher.Current;
        if (SelectedTreeId is int treeId)
        {
            for (var i = 0; i < snap.Trees.Count; i++)
            {
                var t = snap.Trees[i];
                if (t.EntityId != treeId) continue;
                _commands.Submit(new EraseInRectCommand(
                    new TileRect(t.TileX, t.TileY, t.TileX, t.TileY)));
                return;
            }
            return;
        }
        if (SelectedBoulderId is int boulderId)
        {
            for (var i = 0; i < snap.Boulders.Count; i++)
            {
                var b = snap.Boulders[i];
                if (b.EntityId != boulderId) continue;
                _commands.Submit(new EraseInRectCommand(
                    new TileRect(b.TileX, b.TileY, b.TileX, b.TileY)));
                return;
            }
            return;
        }
        if (SelectedStructureId is int structId)
        {
            for (var i = 0; i < snap.Structures.Count; i++)
            {
                var s = snap.Structures[i];
                if (s.EntityId != structId) continue;
                // Cancel any uninstall/deconstruct designation on this tile —
                // EraseInRect drops designations + blueprints in the rect.
                _commands.Submit(new EraseInRectCommand(
                    new TileRect(s.TileX, s.TileY, s.TileX, s.TileY)));
                return;
            }
        }
    }

    private void DropStaleItemSelection()
    {
        if (SelectedItemId is not int id) return;
        var items = _publisher.Current.Items;
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].EntityId == id) return;
        }
        SelectedItemId = null;
        _contextMenu?.CloseIfOpen();
        SelectionChanged?.Invoke();
    }

    public override void _UnhandledInput(InputEvent ev)
    {
        if (ev is InputEventKey ke && ke.Pressed && !ke.Echo)
        {
            HandleSelectionShortcut(ke);
            return;
        }
        if (ev is not InputEventMouseButton mb || !mb.Pressed) return;
        if (_tools is not null && !string.IsNullOrEmpty(_tools.ActiveToolId)) return;

        var camera = GetViewport().GetCamera3D();
        if (camera is null) return;

        var groundHit = TerrainRayCast.Project(camera, mb.Position, _heightfield);
        if (groundHit is null) return;
        var hit = groundHit.Value;

        if (mb.ButtonIndex == MouseButton.Left)
        {
            if (SelectColonistNearRay(camera, mb.Position)) return;
            if (SelectTreeNearRay(camera, mb.Position)) return;
            if (SelectBoulderNearRay(camera, mb.Position)) return;
            if (SelectItemNearRay(camera, mb.Position)) return;
            var tx = (int)MathF.Floor(hit.X / SimConstants.GodotUnitsPerTile);
            var ty = (int)MathF.Floor(hit.Z / SimConstants.GodotUnitsPerTile);
            if (SelectStructureAtTile(tx, ty)) return;
            if (SelectBlueprintAtTile(tx, ty)) return;
            SelectZoneAtTile(tx, ty);
        }
        else if (mb.ButtonIndex == MouseButton.Right)
        {
            if (TryOpenTreeContextMenu(camera, mb.Position)) return;
            if (TryOpenBoulderContextMenu(camera, mb.Position)) return;
            if (TryOpenItemContextMenu(camera, mb.Position)) return;
            if (SelectedEntityId is int id)
            {
                var tx = (int)MathF.Floor(hit.X / SimConstants.GodotUnitsPerTile);
                var ty = (int)MathF.Floor(hit.Z / SimConstants.GodotUnitsPerTile);
                tx = Mathf.Clamp(tx, 0, TilesPerCell - 1);
                ty = Mathf.Clamp(ty, 0, TilesPerCell - 1);
                _commands.Submit(new MoveCommand(id, new TileCoord(tx, ty)));
                SimLog.Logger.Information("Move command for entity {Id} -> ({TX},{TY}).", id, tx, ty);
            }
        }
    }

    private void SelectZoneAtTile(int tx, int ty)
    {
        var snap = _publisher.Current;
        for (var i = 0; i < snap.Zones.Count; i++)
        {
            var z = snap.Zones[i];
            if (!z.ContainsTile(tx, ty)) continue;
            ClearOthersExceptZone();
            if (SelectedZoneId == z.ZoneId) return;
            SelectedZoneId = z.ZoneId;
            SelectionChanged?.Invoke();
            return;
        }
        ClearAll();
    }

    private void ClearOthersExceptZone()
    {
        var changed = false;
        if (SelectedEntityId is not null) { SelectedEntityId = null; changed = true; }
        if (SelectedTreeId is not null) { SelectedTreeId = null; changed = true; }
        if (SelectedBoulderId is not null) { SelectedBoulderId = null; changed = true; }
        if (SelectedItemId is not null) { SelectedItemId = null; changed = true; }
        if (SelectedBlueprintId is not null) { SelectedBlueprintId = null; changed = true; }
        if (SelectedStructureId is not null) { SelectedStructureId = null; changed = true; }
        if (changed) SelectionChanged?.Invoke();
    }

    private void ClearAll()
    {
        var changed = SelectedEntityId is not null || SelectedZoneId is not null
            || SelectedTreeId is not null || SelectedBoulderId is not null || SelectedItemId is not null
            || SelectedBlueprintId is not null || SelectedStructureId is not null;
        SelectedEntityId = null;
        SelectedZoneId = null;
        SelectedTreeId = null;
        SelectedBoulderId = null;
        SelectedItemId = null;
        SelectedBlueprintId = null;
        SelectedStructureId = null;
        if (changed) SelectionChanged?.Invoke();
    }

    private bool SelectStructureAtTile(int tx, int ty)
    {
        var snap = _publisher.Current;
        var bestId = 0;
        var bestLayer = -1;
        for (var i = 0; i < snap.Structures.Count; i++)
        {
            var s = snap.Structures[i];
            if (!Sim.Blueprints.BlueprintCatalog.TryGet(s.DefId, out var def) || def is null) continue;
            var (w, h) = (s.Rotation & 1) == 0 ? (def.FootprintW, def.FootprintH) : (def.FootprintH, def.FootprintW);
            if (tx < s.TileX || ty < s.TileY || tx >= s.TileX + w || ty >= s.TileY + h) continue;
            if (s.BaseLayer > bestLayer) { bestLayer = s.BaseLayer; bestId = s.EntityId; }
        }
        if (bestId == 0) return false;
        if (SelectedStructureId == bestId) return true;
        SelectedStructureId = bestId;
        SelectedBlueprintId = null;
        SelectedEntityId = null;
        SelectedZoneId = null;
        SelectedTreeId = null;
        SelectedBoulderId = null;
        SelectedItemId = null;
        SelectionChanged?.Invoke();
        return true;
    }

    private bool SelectBlueprintAtTile(int tx, int ty)
    {
        var snap = _publisher.Current;
        var bestId = 0;
        var bestLayer = -1;
        for (var i = 0; i < snap.BlueprintGhosts.Count; i++)
        {
            var g = snap.BlueprintGhosts[i];
            if (!Sim.Blueprints.BlueprintCatalog.TryGet(g.DefId, out var def) || def is null) continue;
            var (w, h) = (g.Rotation & 1) == 0 ? (def.FootprintW, def.FootprintH) : (def.FootprintH, def.FootprintW);
            if (tx < g.OriginTileX || ty < g.OriginTileY || tx >= g.OriginTileX + w || ty >= g.OriginTileY + h) continue;
            if (g.BaseLayer > bestLayer) { bestLayer = g.BaseLayer; bestId = g.EntityId; }
        }
        if (bestId == 0) return false;
        if (SelectedBlueprintId == bestId) return true;
        SelectedBlueprintId = bestId;
        SelectedStructureId = null;
        SelectedEntityId = null;
        SelectedZoneId = null;
        SelectedTreeId = null;
        SelectedBoulderId = null;
        SelectedItemId = null;
        SelectionChanged?.Invoke();
        return true;
    }

    private bool TryOpenTreeContextMenu(Camera3D camera, Vector2 mousePos)
    {
        if (_contextMenu is null) return false;
        var id = PickTreeId(camera, mousePos);
        if (id is null) return false;
        _contextMenu.OpenForTree(id.Value, mousePos);
        return true;
    }

    private int? PickTreeId(Camera3D camera, Vector2 mousePos)
    {
        var origin = camera.ProjectRayOrigin(mousePos);
        var dir = camera.ProjectRayNormal(mousePos).Normalized();

        var snap = _publisher.Current;
        var best = -1;
        var bestDist = float.PositiveInfinity;
        var radiusUnits = 1.0f * _unitsPerMeter;
        var heightUnits = 6.0f * _unitsPerMeter;
        for (var i = 0; i < snap.Trees.Count; i++)
        {
            var t = snap.Trees[i];
            var metersX = (t.TileX + 0.5f) * SimConstants.MetersPerTile;
            var metersY = (t.TileY + 0.5f) * SimConstants.MetersPerTile;
            var cx = metersX * _unitsPerMeter;
            var cz = metersY * _unitsPerMeter;
            var y0 = SampleGroundUnits(metersX, metersY);
            var y1 = y0 + heightUnits;
            if (!RayCylinderHit(origin, dir, cx, cz, radiusUnits, y0, y1, out var tHit)) continue;
            if (tHit < bestDist)
            {
                bestDist = tHit;
                best = t.EntityId;
            }
        }
        return best == -1 ? null : best;
    }

    private bool TryOpenBoulderContextMenu(Camera3D camera, Vector2 mousePos)
    {
        if (_contextMenu is null) return false;
        var id = PickBoulderId(camera, mousePos);
        if (id is null) return false;
        _contextMenu.OpenForBoulder(id.Value, mousePos);
        return true;
    }

    private bool SelectBoulderNearRay(Camera3D camera, Vector2 mousePos)
    {
        var id = PickBoulderId(camera, mousePos);
        if (id is null) return false;
        if (SelectedBoulderId == id) return true;
        SelectedBoulderId = id;
        if (SelectedEntityId is not null) SelectedEntityId = null;
        if (SelectedZoneId is not null) SelectedZoneId = null;
        if (SelectedTreeId is not null) SelectedTreeId = null;
        if (SelectedItemId is not null) SelectedItemId = null;
        SelectionChanged?.Invoke();
        return true;
    }

    private int? PickBoulderId(Camera3D camera, Vector2 mousePos)
    {
        var origin = camera.ProjectRayOrigin(mousePos);
        var dir = camera.ProjectRayNormal(mousePos).Normalized();

        var snap = _publisher.Current;
        var best = -1;
        var bestDist = float.PositiveInfinity;
        // Boulders are roughly tile-sized rocks. Cylinder radius ~0.6m so
        // oblique clicks still hit, height ~1.2m to clear the tallest variant.
        var radiusUnits = 0.6f * _unitsPerMeter;
        var heightUnits = 1.2f * _unitsPerMeter;
        for (var i = 0; i < snap.Boulders.Count; i++)
        {
            var b = snap.Boulders[i];
            var metersX = (b.TileX + 0.5f) * SimConstants.MetersPerTile;
            var metersY = (b.TileY + 0.5f) * SimConstants.MetersPerTile;
            var cx = metersX * _unitsPerMeter;
            var cz = metersY * _unitsPerMeter;
            var y0 = SampleGroundUnits(metersX, metersY);
            var y1 = y0 + heightUnits;
            if (!RayCylinderHit(origin, dir, cx, cz, radiusUnits, y0, y1, out var tHit)) continue;
            if (tHit < bestDist)
            {
                bestDist = tHit;
                best = b.EntityId;
            }
        }
        return best == -1 ? null : best;
    }

    private bool TryOpenItemContextMenu(Camera3D camera, Vector2 mousePos)
    {
        if (_contextMenu is null) return false;
        var id = PickItemId(camera, mousePos);
        if (id is null) return false;
        _contextMenu.OpenForItem(id.Value, mousePos);
        return true;
    }

    private bool SelectItemNearRay(Camera3D camera, Vector2 mousePos)
    {
        var id = PickItemId(camera, mousePos);
        if (id is null) return false;
        if (SelectedItemId == id) return true;
        SelectedItemId = id;
        if (SelectedEntityId is not null) SelectedEntityId = null;
        if (SelectedZoneId is not null) SelectedZoneId = null;
        if (SelectedTreeId is not null) SelectedTreeId = null;
        if (SelectedBoulderId is not null) SelectedBoulderId = null;
        SelectionChanged?.Invoke();
        return true;
    }

    private int? PickItemId(Camera3D camera, Vector2 mousePos)
    {
        var origin = camera.ProjectRayOrigin(mousePos);
        var dir = camera.ProjectRayNormal(mousePos).Normalized();

        var snap = _publisher.Current;
        var best = -1;
        var bestDist = float.PositiveInfinity;
        var radiusUnits = 0.5f * _unitsPerMeter;
        var heightUnits = 0.6f * _unitsPerMeter;
        for (var i = 0; i < snap.Items.Count; i++)
        {
            var it = snap.Items[i];
            var metersX = (it.TileX + 0.5f) * SimConstants.MetersPerTile;
            var metersY = (it.TileY + 0.5f) * SimConstants.MetersPerTile;
            var cx = metersX * _unitsPerMeter;
            var cz = metersY * _unitsPerMeter;
            var y0 = SampleGroundUnits(metersX, metersY);
            var y1 = y0 + heightUnits;
            if (!RayCylinderHit(origin, dir, cx, cz, radiusUnits, y0, y1, out var tHit)) continue;
            if (tHit < bestDist)
            {
                bestDist = tHit;
                best = it.EntityId;
            }
        }
        return best == -1 ? null : best;
    }

    private bool SelectTreeNearRay(Camera3D camera, Vector2 mousePos)
    {
        // Real ray-vs-vertical-cylinder picker is in PickTreeId — old
        // perpendicular-foot test made oblique camera clicks miss.
        var id = PickTreeId(camera, mousePos);
        if (id is null) return false;
        if (SelectedTreeId == id) return true;
        SelectedTreeId = id;
        if (SelectedEntityId is not null) SelectedEntityId = null;
        if (SelectedZoneId is not null) SelectedZoneId = null;
        if (SelectedBoulderId is not null) SelectedBoulderId = null;
        if (SelectedItemId is not null) SelectedItemId = null;
        SelectionChanged?.Invoke();
        return true;
    }

    // Select a colonist by entity id directly — used by the portrait bar
    // when the player clicks a portrait. Mirrors SelectColonistNearRay's
    // clearing behavior so other selections drop together.
    public void SelectColonist(int entityId)
    {
        if (SelectedEntityId == entityId) return;
        SelectedEntityId = entityId;
        if (SelectedZoneId is not null) SelectedZoneId = null;
        if (SelectedTreeId is not null) SelectedTreeId = null;
        if (SelectedBoulderId is not null) SelectedBoulderId = null;
        if (SelectedItemId is not null) SelectedItemId = null;
        if (SelectedBlueprintId is not null) SelectedBlueprintId = null;
        if (SelectedStructureId is not null) SelectedStructureId = null;
        SelectionChanged?.Invoke();
    }

    private bool SelectColonistNearRay(Camera3D camera, Vector2 mousePos)
    {
        var origin = camera.ProjectRayOrigin(mousePos);
        var dir = camera.ProjectRayNormal(mousePos).Normalized();

        var snap = _publisher.Current;
        var best = -1;
        var bestDist = float.PositiveInfinity;

        for (var i = 0; i < snap.Colonists.Count; i++)
        {
            var c = snap.Colonists[i];
            var x = c.MetersX * _unitsPerMeter;
            var z = c.MetersY * _unitsPerMeter;
            var y = SampleGroundUnits(c.MetersX, c.MetersY) + 24f;
            var p = new Vector3(x, y, z);
            var toP = p - origin;
            var t = toP.Dot(dir);
            if (t < 0f) continue;
            var closest = origin + dir * t;
            var d = closest.DistanceTo(p);
            if (d < bestDist && d < ColonistPickRadiusUnits)
            {
                bestDist = d;
                best = c.EntityId;
            }
        }

        if (best == -1) return false;
        SelectedEntityId = best;
        if (SelectedZoneId is not null) SelectedZoneId = null;
        if (SelectedTreeId is not null) SelectedTreeId = null;
        if (SelectedBoulderId is not null) SelectedBoulderId = null;
        if (SelectedItemId is not null) SelectedItemId = null;
        SelectionChanged?.Invoke();
        return true;
    }

    private static bool RayCylinderHit(
        Vector3 origin, Vector3 dir,
        float cx, float cz, float radius, float y0, float y1,
        out float tHit)
    {
        tHit = 0f;
        var ox = origin.X - cx;
        var oz = origin.Z - cz;
        var a = dir.X * dir.X + dir.Z * dir.Z;
        var b = 2f * (ox * dir.X + oz * dir.Z);
        var c = ox * ox + oz * oz - radius * radius;

        float tEnter, tExit;
        if (a < 1e-6f)
        {
            // Ray is vertical: only inside cylinder if origin already is.
            if (c > 0f) return false;
            tEnter = float.NegativeInfinity;
            tExit = float.PositiveInfinity;
        }
        else
        {
            var disc = b * b - 4f * a * c;
            if (disc < 0f) return false;
            var sq = MathF.Sqrt(disc);
            tEnter = (-b - sq) / (2f * a);
            tExit = (-b + sq) / (2f * a);
        }

        // Clip against horizontal caps so we only count t-values where the
        // ray is *also* inside the cylinder's vertical extent.
        if (MathF.Abs(dir.Y) > 1e-6f)
        {
            var tCap0 = (y0 - origin.Y) / dir.Y;
            var tCap1 = (y1 - origin.Y) / dir.Y;
            if (tCap0 > tCap1) (tCap0, tCap1) = (tCap1, tCap0);
            tEnter = MathF.Max(tEnter, tCap0);
            tExit = MathF.Min(tExit, tCap1);
        }
        else
        {
            if (origin.Y < y0 || origin.Y > y1) return false;
        }

        if (tExit < 0f || tEnter > tExit) return false;
        tHit = tEnter > 0f ? tEnter : 0f;
        return true;
    }

    private float SampleGroundUnits(float metersX, float metersY)
    {
        var tilesX = metersX / SimConstants.MetersPerTile;
        var tilesY = metersY / SimConstants.MetersPerTile;
        return _heightfield.SurfaceMetresAt(tilesX, tilesY) * _unitsPerMeter;
    }
}
