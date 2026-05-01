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
    // LMB-press to LMB-release moves of less than this many pixels stay
    // a click; any further and it's a drag-rect selection. Tuned by feel
    // — too small misfires drag from camera-shake, too large breaks small
    // rect selects. 8px works at 1080p and 1440p.
    private const float DragThresholdPx = 8f;

    private SnapshotPublisher _publisher = null!;
    private CommandBus _commands = null!;
    private Heightfield _heightfield = null!;
    private BuildToolService? _tools;
    private ContextMenu? _contextMenu;
    private ScreenSelectionOverlay? _screenOverlay;
    private PortraitBar? _portraitBar;
    private float _unitsPerMeter;

    private Vector2? _lmbDragStartScreen;

    public int? SelectedEntityId { get; private set; }
    public int? SelectedZoneId { get; private set; }
    public int? SelectedTreeId { get; private set; }
    public int? SelectedBoulderId { get; private set; }
    public int? SelectedItemId { get; private set; }
    public int? SelectedBlueprintId { get; private set; }
    public int? SelectedStructureId { get; private set; }
    public Vector2? SelectedGroundXZUnits { get; private set; }

    // Multi-select for colonists (drag-rect, shift-click). The primary
    // SelectedEntityId is always one of these when non-empty — info panel
    // and context menu still target the primary, but mass commands
    // (draft, move, prioritize) iterate the full set.
    public HashSet<int> SelectedColonistIds { get; } = new();

    public event System.Action? SelectionChanged;

    public void Configure(SnapshotPublisher publisher, CommandBus commands, Heightfield heightfield)
    {
        _publisher = publisher;
        _commands = commands;
        _heightfield = heightfield;
    }

    public void SetBuildTools(BuildToolService tools) => _tools = tools;

    public void SetContextMenu(ContextMenu menu) => _contextMenu = menu;

    public void SetScreenOverlay(ScreenSelectionOverlay overlay) => _screenOverlay = overlay;

    public void SetPortraitBar(PortraitBar bar) => _portraitBar = bar;

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
            case Key.R:
                ApplyDraftToggleShortcut();
                break;
        }
    }

    private void ApplyDraftToggleShortcut()
    {
        if (SelectedEntityId is not int id) return;
        var snap = _publisher.Current;
        for (var i = 0; i < snap.Colonists.Count; i++)
        {
            var c = snap.Colonists[i];
            if (c.EntityId != id) continue;
            _commands.Submit(new SetDraftedCommand(new[] { id }, !c.Drafted));
            return;
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
            _commands.Submit(new DeconstructStructureCommand(structId, _tools?.GodMode == true));
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
            return;
        }
        if (SelectedBlueprintId is int bpId)
        {
            for (var i = 0; i < snap.BlueprintGhosts.Count; i++)
            {
                var g = snap.BlueprintGhosts[i];
                if (g.EntityId != bpId) continue;
                if (!Sim.Blueprints.BlueprintCatalog.TryGet(g.DefId, out var def) || def is null) continue;
                var (w, h) = (g.Rotation & 1) == 0 ? (def.FootprintW, def.FootprintH) : (def.FootprintH, def.FootprintW);
                _commands.Submit(new EraseInRectCommand(new TileRect(
                    g.OriginTileX, g.OriginTileY,
                    g.OriginTileX + w - 1, g.OriginTileY + h - 1)));
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

    // _Input runs before GUI processing, so we can detect a drag-start over
    // a portrait button before the button captures the click. We track
    // press/motion/release here and only AcceptEvent (suppress GUI) when a
    // real drag commits — a quick tap stays free to fire portrait clicks
    // and the regular pick chain in _UnhandledInput.
    public override void _Input(InputEvent ev)
    {
        if (_tools is not null && !string.IsNullOrEmpty(_tools.ActiveToolId))
        {
            CancelLmbDrag();
            return;
        }
        if (ev is InputEventMouseMotion mm)
        {
            UpdateSelectionDragPreview(mm.Position);
            return;
        }
        if (ev is not InputEventMouseButton mb) return;
        if (mb.ButtonIndex != MouseButton.Left) return;

        if (mb.Pressed)
        {
            _lmbDragStartScreen = mb.Position;
            return;
        }

        var startScreen = _lmbDragStartScreen;
        _lmbDragStartScreen = null;
        if (_screenOverlay is not null) _screenOverlay.PreviewRect = null;
        if (startScreen is null) return;

        var dragDist = (mb.Position - startScreen.Value).Length();
        if (dragDist < DragThresholdPx) return;

        var camera = GetViewport().GetCamera3D();
        if (camera is null) return;
        var rect = MakeScreenRect(startScreen.Value, mb.Position);
        DragRectSelectColonists(camera, rect, mb.ShiftPressed);
        // Eat the release so the portrait button under the release point
        // doesn't also fire a single-select.
        GetViewport().SetInputAsHandled();
    }

    public override void _UnhandledInput(InputEvent ev)
    {
        if (ev is InputEventKey ke && ke.Pressed && !ke.Echo)
        {
            HandleSelectionShortcut(ke);
            return;
        }
        if (_tools is not null && !string.IsNullOrEmpty(_tools.ActiveToolId)) return;
        if (ev is not InputEventMouseButton mb) return;

        var camera = GetViewport().GetCamera3D();
        if (camera is null) return;

        if (mb.ButtonIndex == MouseButton.Left && !mb.Pressed)
        {
            // _Input already discarded the drag-start; remaining LMB releases
            // here are taps on the world (UI didn't consume). Run the pick.
            var groundHit = TerrainRayCast.Project(camera, mb.Position, _heightfield);
            if (groundHit is null) return;
            HandleLeftClickPick(camera, mb.Position, groundHit.Value, mb.ShiftPressed);
            return;
        }
        if (mb.ButtonIndex == MouseButton.Right && mb.Pressed)
        {
            HandleRightMouse(camera, mb);
        }
    }

    private bool IsColonist(int entityId)
    {
        var snap = _publisher.Current;
        for (var i = 0; i < snap.Colonists.Count; i++)
        {
            if (snap.Colonists[i].EntityId == entityId) return true;
        }
        return false;
    }

    private static Rect2 MakeScreenRect(Vector2 a, Vector2 b)
    {
        var min = new Vector2(MathF.Min(a.X, b.X), MathF.Min(a.Y, b.Y));
        var max = new Vector2(MathF.Max(a.X, b.X), MathF.Max(a.Y, b.Y));
        return new Rect2(min, max - min);
    }

    private void HandleRightMouse(Camera3D camera, InputEventMouseButton mb)
    {
        var groundHit = TerrainRayCast.Project(camera, mb.Position, _heightfield);
        if (groundHit is null) return;
        var hit = groundHit.Value;

        // Shift-RMB on a colonist selection means "queue move waypoint" —
        // skip the prioritize/build/haul context menus so the gesture
        // always falls through to the move chain logic below.
        var hasColonistSelection = SelectedColonistIds.Count > 0
            || (SelectedEntityId is int candidate && IsColonist(candidate));
        if (!(mb.ShiftPressed && hasColonistSelection))
        {
            if (TryOpenTreeContextMenu(camera, mb.Position)) return;
            if (TryOpenBoulderContextMenu(camera, mb.Position)) return;
            if (TryOpenItemContextMenu(camera, mb.Position)) return;
            if (TryOpenBlueprintContextMenu(camera, mb.Position)) return;
        }

        // Move every selected colonist if multi-selected; fall back to
        // the primary so single-select right-click still moves one.
        var ids = SelectedColonistIds.Count > 0
            ? new List<int>(SelectedColonistIds)
            : (SelectedEntityId is int id ? new List<int> { id } : new List<int>());
        if (ids.Count == 0) return;

        var tx = Mathf.Clamp((int)MathF.Floor(hit.X / SimConstants.GodotUnitsPerTile), 0, TilesPerCell - 1);
        var ty = Mathf.Clamp((int)MathF.Floor(hit.Z / SimConstants.GodotUnitsPerTile), 0, TilesPerCell - 1);
        var queue = mb.ShiftPressed;
        for (var i = 0; i < ids.Count; i++)
        {
            _commands.Submit(new MoveCommand(ids[i], new TileCoord(tx, ty), queue));
        }
        SimLog.Logger.Information("Move command for {N} entity(ies) -> ({TX},{TY}) queue={Q}.",
            ids.Count, tx, ty, queue);
    }

    private void HandleLeftClickPick(Camera3D camera, Vector2 mousePos, Vector3 groundHit, bool shift)
    {
        var colonistId = PickColonistId(camera, mousePos);
        if (colonistId is not null)
        {
            if (shift) ToggleColonistInMulti(colonistId.Value);
            else SetSingleColonistSelection(colonistId.Value);
            return;
        }

        // Shift on a non-colonist target is a no-op for now — multi-select
        // beyond colonists isn't wired and we don't want shift to silently
        // clobber the colonist multi.
        if (shift) return;

        SelectedColonistIds.Clear();
        if (SelectTreeNearRay(camera, mousePos)) return;
        if (SelectBoulderNearRay(camera, mousePos)) return;
        if (SelectItemNearRay(camera, mousePos)) return;
        if (SelectBlueprintNearRay(camera, mousePos)) return;
        var tx = (int)MathF.Floor(groundHit.X / SimConstants.GodotUnitsPerTile);
        var ty = (int)MathF.Floor(groundHit.Z / SimConstants.GodotUnitsPerTile);
        if (SelectStructureAtTile(tx, ty)) return;
        if (SelectBlueprintAtTile(tx, ty)) return;
        SelectZoneAtTile(tx, ty);
    }

    private int? PickColonistId(Camera3D camera, Vector2 mousePos)
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
        return best == -1 ? null : best;
    }

    private void SetSingleColonistSelection(int id)
    {
        SelectedColonistIds.Clear();
        SelectedColonistIds.Add(id);
        SelectedEntityId = id;
        ClearNonColonistSelections();
        SelectionChanged?.Invoke();
    }

    public void ToggleColonistSelection(int id) => ToggleColonistInMulti(id);

    private void ToggleColonistInMulti(int id)
    {
        if (SelectedColonistIds.Contains(id))
        {
            SelectedColonistIds.Remove(id);
            if (SelectedEntityId == id)
            {
                SelectedEntityId = null;
                foreach (var remaining in SelectedColonistIds) { SelectedEntityId = remaining; break; }
            }
        }
        else
        {
            SelectedColonistIds.Add(id);
            SelectedEntityId = id;
        }
        ClearNonColonistSelections();
        SelectionChanged?.Invoke();
    }

    private void DragRectSelectColonists(Camera3D camera, Rect2 screenRect, bool shift)
    {
        var snap = _publisher.Current;
        var hits = new List<int>();
        var seen = new HashSet<int>();

        // World colonists: project each to screen and test against the rect.
        // Colonists behind the camera (Z < 0 in clip space) get a negative
        // depth from UnprojectPosition isn't available, so we test with a
        // ray-direction sign instead.
        for (var i = 0; i < snap.Colonists.Count; i++)
        {
            var c = snap.Colonists[i];
            var x = c.MetersX * _unitsPerMeter;
            var z = c.MetersY * _unitsPerMeter;
            var y = SampleGroundUnits(c.MetersX, c.MetersY) + 24f;
            var world = new Vector3(x, y, z);
            if (camera.IsPositionBehind(world)) continue;
            var screen = camera.UnprojectPosition(world);
            if (!screenRect.HasPoint(screen)) continue;
            if (seen.Add(c.EntityId)) hits.Add(c.EntityId);
        }

        // Portrait drag: any portrait whose global rect overlaps the screen
        // rect counts as picked, even when the colonist itself is offscreen.
        if (_portraitBar is not null)
        {
            foreach (var (entityId, portraitRect) in _portraitBar.GetPortraitGlobalRects())
            {
                if (!screenRect.Intersects(portraitRect)) continue;
                if (seen.Add(entityId)) hits.Add(entityId);
            }
        }

        if (hits.Count == 0)
        {
            // Empty rect with no shift = clear; with shift = keep prior.
            if (!shift) ClearAll();
            return;
        }
        if (!shift) SelectedColonistIds.Clear();
        for (var i = 0; i < hits.Count; i++) SelectedColonistIds.Add(hits[i]);
        SelectedEntityId = hits[0];
        ClearNonColonistSelections();
        SelectionChanged?.Invoke();
    }

    private void ClearNonColonistSelections()
    {
        SelectedZoneId = null;
        SelectedTreeId = null;
        SelectedBoulderId = null;
        SelectedItemId = null;
        SelectedBlueprintId = null;
        SelectedStructureId = null;
    }

    private void UpdateSelectionDragPreview(Vector2 mousePos)
    {
        if (_lmbDragStartScreen is null || _screenOverlay is null) return;
        if ((mousePos - _lmbDragStartScreen.Value).Length() < DragThresholdPx)
        {
            _screenOverlay.PreviewRect = null;
            return;
        }
        _screenOverlay.PreviewRect = MakeScreenRect(_lmbDragStartScreen.Value, mousePos);
    }

    private void CancelLmbDrag()
    {
        if (_lmbDragStartScreen is null) return;
        _lmbDragStartScreen = null;
        if (_screenOverlay is not null) _screenOverlay.PreviewRect = null;
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
        if (SelectedColonistIds.Count > 0) { SelectedColonistIds.Clear(); changed = true; }
        if (changed) SelectionChanged?.Invoke();
    }

    private void ClearAll()
    {
        var changed = SelectedEntityId is not null || SelectedZoneId is not null
            || SelectedTreeId is not null || SelectedBoulderId is not null || SelectedItemId is not null
            || SelectedBlueprintId is not null || SelectedStructureId is not null
            || SelectedColonistIds.Count > 0;
        SelectedEntityId = null;
        SelectedZoneId = null;
        SelectedTreeId = null;
        SelectedBoulderId = null;
        SelectedItemId = null;
        SelectedBlueprintId = null;
        SelectedStructureId = null;
        SelectedColonistIds.Clear();
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

    private bool SelectBlueprintNearRay(Camera3D camera, Vector2 mousePos)
    {
        var id = PickBlueprintId(camera, mousePos);
        if (id is null) return false;
        if (SelectedBlueprintId == id) return true;
        SelectedBlueprintId = id;
        SelectedStructureId = null;
        SelectedEntityId = null;
        SelectedZoneId = null;
        SelectedTreeId = null;
        SelectedBoulderId = null;
        SelectedItemId = null;
        SelectionChanged?.Invoke();
        return true;
    }

    // Ray-vs-blueprint-AABB pick. Each ghost's box spans its full footprint
    // in X/Z and its def HeightMeters in Y, with the floor sampled from the
    // heightfield at the footprint center. Pick the closest hit, tie-break
    // by higher BaseLayer so an upper ghost wins over a lower one stacked
    // under it.
    private int? PickBlueprintId(Camera3D camera, Vector2 mousePos)
    {
        var origin = camera.ProjectRayOrigin(mousePos);
        var dir = camera.ProjectRayNormal(mousePos).Normalized();

        var snap = _publisher.Current;
        var bestId = -1;
        var bestT = float.PositiveInfinity;
        var bestLayer = int.MinValue;

        for (var i = 0; i < snap.BlueprintGhosts.Count; i++)
        {
            var g = snap.BlueprintGhosts[i];
            if (!Sim.Blueprints.BlueprintCatalog.TryGet(g.DefId, out var def) || def is null) continue;
            var (w, h) = (g.Rotation & 1) == 0 ? (def.FootprintW, def.FootprintH) : (def.FootprintH, def.FootprintW);
            var xMin = g.OriginTileX * SimConstants.GodotUnitsPerTile;
            var xMax = (g.OriginTileX + w) * SimConstants.GodotUnitsPerTile;
            var zMin = g.OriginTileY * SimConstants.GodotUnitsPerTile;
            var zMax = (g.OriginTileY + h) * SimConstants.GodotUnitsPerTile;
            var centerMetersX = (g.OriginTileX + w * 0.5f) * SimConstants.MetersPerTile;
            var centerMetersY = (g.OriginTileY + h * 0.5f) * SimConstants.MetersPerTile;
            var groundY = SampleGroundUnits(centerMetersX, centerMetersY);
            // BaseLayer steps in 0.75 m vertical quanta — match the build
            // layer step so a half-stacked ghost picks against the right Y
            // band, not always the ground.
            var baseY = groundY + g.BaseLayer * 0.75f * _unitsPerMeter;
            var heightUnits = MathF.Max(0.25f, def.HeightMeters) * _unitsPerMeter;
            var yMin = baseY;
            var yMax = baseY + heightUnits;
            if (!RayAabbHit(origin, dir, xMin, yMin, zMin, xMax, yMax, zMax, out var tHit)) continue;
            if (tHit < bestT || (Mathf.IsEqualApprox(tHit, bestT) && g.BaseLayer > bestLayer))
            {
                bestT = tHit;
                bestLayer = g.BaseLayer;
                bestId = g.EntityId;
            }
        }
        return bestId == -1 ? null : bestId;
    }

    private static bool RayAabbHit(
        Vector3 origin, Vector3 dir,
        float xMin, float yMin, float zMin,
        float xMax, float yMax, float zMax,
        out float tHit)
    {
        tHit = 0f;
        var tEnter = float.NegativeInfinity;
        var tExit = float.PositiveInfinity;
        if (!SlabClip(origin.X, dir.X, xMin, xMax, ref tEnter, ref tExit)) return false;
        if (!SlabClip(origin.Y, dir.Y, yMin, yMax, ref tEnter, ref tExit)) return false;
        if (!SlabClip(origin.Z, dir.Z, zMin, zMax, ref tEnter, ref tExit)) return false;
        if (tExit < 0f || tEnter > tExit) return false;
        tHit = tEnter > 0f ? tEnter : 0f;
        return true;
    }

    private static bool SlabClip(float ro, float rd, float lo, float hi, ref float tEnter, ref float tExit)
    {
        if (MathF.Abs(rd) < 1e-6f) return ro >= lo && ro <= hi;
        var inv = 1f / rd;
        var t0 = (lo - ro) * inv;
        var t1 = (hi - ro) * inv;
        if (t0 > t1) (t0, t1) = (t1, t0);
        if (t0 > tEnter) tEnter = t0;
        if (t1 < tExit) tExit = t1;
        return tEnter <= tExit;
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

    private bool TryOpenBlueprintContextMenu(Camera3D camera, Vector2 mousePos)
    {
        if (_contextMenu is null) return false;
        var id = PickBlueprintId(camera, mousePos);
        if (id is null) return false;
        _contextMenu.OpenForBlueprint(id.Value, mousePos);
        return true;
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
    // when the player clicks a portrait. Replaces any prior multi.
    public void SelectColonist(int entityId)
    {
        if (SelectedEntityId == entityId && SelectedColonistIds.Count == 1
            && SelectedColonistIds.Contains(entityId)) return;
        SelectedColonistIds.Clear();
        SelectedColonistIds.Add(entityId);
        SelectedEntityId = entityId;
        if (SelectedZoneId is not null) SelectedZoneId = null;
        if (SelectedTreeId is not null) SelectedTreeId = null;
        if (SelectedBoulderId is not null) SelectedBoulderId = null;
        if (SelectedItemId is not null) SelectedItemId = null;
        if (SelectedBlueprintId is not null) SelectedBlueprintId = null;
        if (SelectedStructureId is not null) SelectedStructureId = null;
        SelectionChanged?.Invoke();
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
