using CowColonySim.Game.Terrain;
using CowColonySim.Sim;
using CowColonySim.Sim.Blueprints;
using CowColonySim.Sim.Commands;
using CowColonySim.Sim.Designations;
using CowColonySim.Sim.Snapshots;
using CowColonySim.Sim.Terrain;
using CowColonySim.Sim.Zones;
using Godot;

namespace CowColonySim.Game.UI;

// Owns left-click placement for zones, designators, blueprints, and the
// erase tool. Reads BuildToolService.ActiveToolId + BlueprintCatalog
// to decide which mode to run. Drives RectDragOverlay (drag-rect modes)
// and BlueprintGhostPreview (Single/Footprint blueprint modes).
//
// Tool id conventions:
//   zone.<type>          -> drag rect, CreateZoneCommand
//   designate.<kind>     -> drag rect, StampDesignationsCommand
//   blueprint.<defId>    -> Single / LineDrag / Footprint placement
//                           (mode resolved via BlueprintCatalog)
//   edit.erase           -> drag rect, EraseInRectCommand
public partial class PlacementTool : Node
{
    private BuildToolService _tools = null!;
    private RectDragOverlay _rectOverlay = null!;
    private BlueprintGhostPreview _ghostPreview = null!;
    private PowerPlacementPreview? _powerPreview;
    private Heightfield _field = null!;
    private CommandBus _commands = null!;
    private SnapshotPublisher _publisher = null!;

    private Vector2I? _dragStart;
    private int _blueprintRotation;

    public void Configure(
        BuildToolService tools,
        RectDragOverlay rectOverlay,
        BlueprintGhostPreview ghostPreview,
        Heightfield field,
        CommandBus commands,
        SnapshotPublisher publisher)
    {
        _tools = tools;
        _rectOverlay = rectOverlay;
        _ghostPreview = ghostPreview;
        _field = field;
        _commands = commands;
        _publisher = publisher;
    }

    public void SetPowerPreview(PowerPlacementPreview preview) => _powerPreview = preview;

    public override void _Ready()
    {
        _tools.ToolChanged += OnToolChanged;
    }

    private void OnToolChanged(string toolId)
    {
        _dragStart = null;
        _rectOverlay.PreviewRect = null;
        _ghostPreview.DefId = null;
        _powerPreview?.HidePreview();
        _blueprintRotation = 0;
    }

    public override void _UnhandledInput(InputEvent ev)
    {
        var toolId = _tools.ActiveToolId;
        if (string.IsNullOrEmpty(toolId)) return;

        if (toolId.StartsWith("blueprint.") && IsRotateKey(ev))
        {
            _blueprintRotation = (_blueprintRotation + 1) & 3;
            GetViewport().SetInputAsHandled();
            return;
        }

        if (ev is not InputEventMouseButton mb) return;
        if (mb.ButtonIndex != MouseButton.Left) return;

        var tile = ProjectMouseToTile(mb.Position);
        if (tile is null) return;

        if (UsesRectDrag(toolId))
        {
            HandleRectDrag(mb.Pressed, tile.Value, toolId);
            GetViewport().SetInputAsHandled();
            return;
        }

        if (toolId.StartsWith("blueprint.") && mb.Pressed)
        {
            HandleBlueprintClick(tile.Value, toolId);
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Process(double delta)
    {
        var toolId = _tools.ActiveToolId;
        if (string.IsNullOrEmpty(toolId))
        {
            _rectOverlay.PreviewRect = null;
            _ghostPreview.DefId = null;
            return;
        }

        var camera = GetViewport().GetCamera3D();
        Vector2I? hovered = camera is null ? null : ProjectMouseToTile(GetViewport().GetMousePosition());

        if (UsesRectDrag(toolId))
        {
            _ghostPreview.DefId = null;
            UpdateRectPreview(toolId, hovered);
            return;
        }

        if (toolId.StartsWith("blueprint."))
        {
            _rectOverlay.PreviewRect = null;
            var defId = toolId.Substring("blueprint.".Length);
            if (!BlueprintCatalog.TryGet(defId, out var def) || def is null || hovered is null)
            {
                _ghostPreview.DefId = null;
                return;
            }
            var origin = OriginForFootprintCenter(def, _blueprintRotation, hovered.Value);
            var baseLayer = ResolveBaseLayer(def, _blueprintRotation, origin);
            _ghostPreview.DefId = def.Id;
            _ghostPreview.OriginTileX = origin.X;
            _ghostPreview.OriginTileY = origin.Y;
            _ghostPreview.RotationSteps = _blueprintRotation;
            _ghostPreview.BaseLayer = baseLayer;
            _ghostPreview.Valid = IsFootprintPlaceable(def, _blueprintRotation, origin, baseLayer);
        }
    }

    private bool UsesRectDrag(string toolId)
    {
        if (toolId == "edit.erase") return true;
        if (toolId.StartsWith("zone.")) return true;
        if (toolId.StartsWith("designate.")) return true;
        if (toolId.StartsWith("blueprint."))
        {
            var defId = toolId.Substring("blueprint.".Length);
            return BlueprintCatalog.TryGet(defId, out var def) && def is not null
                && (def.Placement == PlacementMode.LineDrag || def.Placement == PlacementMode.SpacedDrag);
        }
        return false;
    }

    private void UpdateRectPreview(string toolId, Vector2I? hovered)
    {
        if (hovered is null && _dragStart is null)
        {
            _rectOverlay.PreviewRect = null;
            _powerPreview?.HidePreview();
            return;
        }
        _rectOverlay.QuadColor = ColorForRectTool(toolId);

        BlueprintDef? spacedDef = null;
        if (_dragStart is not null && hovered is not null)
        {
            var rect = TileRect.FromCorners(
                _dragStart.Value.X, _dragStart.Value.Y, hovered.Value.X, hovered.Value.Y);
            if (toolId.StartsWith("blueprint."))
            {
                var defId = toolId.Substring("blueprint.".Length);
                if (BlueprintCatalog.TryGet(defId, out var def) && def is not null)
                {
                    if (def.Placement == PlacementMode.LineDrag)
                    {
                        rect = AxisAlignedLine(_dragStart.Value, hovered.Value);
                    }
                    else if (def.Placement == PlacementMode.SpacedDrag)
                    {
                        spacedDef = def;
                    }
                }
            }
            _rectOverlay.PreviewRect = rect;
        }
        else if (hovered is not null)
        {
            _rectOverlay.PreviewRect = new TileRect(hovered.Value.X, hovered.Value.Y, hovered.Value.X, hovered.Value.Y);
        }

        if (_powerPreview is not null)
        {
            if (spacedDef is not null && _dragStart is not null && hovered is not null)
            {
                _powerPreview.Show(spacedDef, _dragStart.Value, hovered.Value);
            }
            else
            {
                _powerPreview.HidePreview();
            }
        }
    }

    private void HandleRectDrag(bool pressed, Vector2I tile, string toolId)
    {
        if (pressed)
        {
            _dragStart = tile;
            _rectOverlay.PreviewRect = new TileRect(tile.X, tile.Y, tile.X, tile.Y);
            return;
        }
        if (_dragStart is null) return;
        var start = _dragStart.Value;
        _dragStart = null;
        _rectOverlay.PreviewRect = null;

        var rect = TileRect.FromCorners(start.X, start.Y, tile.X, tile.Y);

        if (toolId == "zone.delete")
        {
            // Reuse EraseInRectCommand: it subtracts the rect from any
            // overlapping zone and deletes zones whose mask becomes empty.
            // It also clears designations + blueprints in the rect, which
            // is the same behavior the dedicated erase tool uses, so the
            // "Delete Zone" button is just a discoverable shortcut.
            _commands.Submit(new EraseInRectCommand(rect));
        }
        else if (toolId.StartsWith("zone."))
        {
            var type = toolId switch
            {
                "zone.stockpile" => ZoneType.Stockpile,
                "zone.farm" => ZoneType.Farm,
                _ => (ZoneType?)null,
            };
            if (type is null) return;
            var name = type == ZoneType.Stockpile ? "Stockpile" : "Farm";
            _commands.Submit(new CreateZoneCommand(type.Value, rect, name));
        }
        else if (toolId.StartsWith("designate."))
        {
            var kind = toolId switch
            {
                "designate.chop_tree" => DesignationKind.ChopTree,
                "designate.mine" => DesignationKind.Mine,
                "designate.harvest" => DesignationKind.Harvest,
                "designate.cut_plant" => DesignationKind.CutPlant,
                _ => (DesignationKind?)null,
            };
            if (kind is null) return;
            _commands.Submit(new StampDesignationsCommand(kind.Value, rect));
        }
        else if (toolId == "edit.erase")
        {
            _commands.Submit(new EraseInRectCommand(rect));
        }
        else if (toolId.StartsWith("blueprint."))
        {
            var defId = toolId.Substring("blueprint.".Length);
            if (!BlueprintCatalog.TryGet(defId, out var def) || def is null) return;
            if (def.Placement == PlacementMode.LineDrag)
            {
                var line = AxisAlignedLine(start, tile);
                for (var y = line.MinY; y <= line.MaxY; y++)
                {
                    for (var x = line.MinX; x <= line.MaxX; x++)
                    {
                        var lineOrigin = new Vector2I(x, y);
                        if (!IsFootprintInBounds(def, 0, lineOrigin)) continue;
                        var lineLayer = ResolveBaseLayer(def, 0, lineOrigin);
                        _commands.Submit(new PlaceBlueprintGhostCommand(def.Id, x, y, 0, lineLayer));
                    }
                }
            }
            else if (def.Placement == PlacementMode.SpacedDrag)
            {
                CommitSpacedDrag(def, start, tile);
            }
        }
    }

    // Walks the straight line from drag start → mouse-up tile, dropping a
    // ghost at each spacing-step (and at the end). When a candidate tile
    // would land somewhere unplaceable (out of bounds, on a slope while
    // baseLayer=0, overlapping a prior ghost), nudge the candidate backward
    // along the line one tile at a time until a free tile is found — never
    // past the previous placed pylon. Start tile has no "previous", so it's
    // skipped if invalid.
    private void CommitSpacedDrag(BlueprintDef def, Vector2I start, Vector2I end)
    {
        var spacing = Math.Max(1, def.DragSpacingTiles);
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var dist = MathF.Sqrt(dx * dx + dy * dy);

        Vector2I? lastPlaced = null;
        var emitted = new HashSet<Vector2I>();

        bool TryEmit(Vector2I pt)
        {
            if (!emitted.Add(pt)) return false;
            if (!IsFootprintInBounds(def, 0, pt)) return false;
            var layer = ResolveBaseLayer(def, 0, pt);
            if (!IsFootprintPlaceable(def, 0, pt, layer)) return false;
            _commands.Submit(new PlaceBlueprintGhostCommand(def.Id, pt.X, pt.Y, 0, layer));
            lastPlaced = pt;
            return true;
        }

        TryEmit(start);

        if (dist <= 0.0001f) return;
        var nx = dx / dist;
        var ny = dy / dist;
        var maxPullback = spacing; // never overshoot into the previous pylon

        bool TryEmitWithPullback(Vector2I ideal)
        {
            for (var back = 0; back <= maxPullback; back++)
            {
                var bx = ideal.X - nx * back;
                var by = ideal.Y - ny * back;
                var pt = new Vector2I((int)MathF.Round(bx), (int)MathF.Round(by));
                if (lastPlaced.HasValue && pt == lastPlaced.Value) return false;
                if (TryEmit(pt)) return true;
            }
            return false;
        }

        var steps = Math.Max(1, (int)MathF.Floor(dist / spacing));
        var endHandledByStep = false;
        for (var i = 1; i <= steps; i++)
        {
            var px = start.X + nx * (spacing * i);
            var py = start.Y + ny * (spacing * i);
            var ideal = new Vector2I((int)MathF.Round(px), (int)MathF.Round(py));
            if (ideal == end) { endHandledByStep = true; break; }
            TryEmitWithPullback(ideal);
        }
        if (!endHandledByStep) TryEmitWithPullback(end);
    }

    private void HandleBlueprintClick(Vector2I tile, string toolId)
    {
        var defId = toolId.Substring("blueprint.".Length);
        if (!BlueprintCatalog.TryGet(defId, out var def) || def is null) return;
        if (def.Placement == PlacementMode.LineDrag) return;

        var origin = OriginForFootprintCenter(def, _blueprintRotation, tile);
        var baseLayer = ResolveBaseLayer(def, _blueprintRotation, origin);
        if (!IsFootprintPlaceable(def, _blueprintRotation, origin, baseLayer)) return;

        _commands.Submit(new PlaceBlueprintGhostCommand(def.Id, origin.X, origin.Y, _blueprintRotation, baseLayer));
    }

    // Reduces a (start, end) pair to a 1-tile-thick rect along the axis
    // with the larger delta — matches how RimWorld-style line-paint
    // commits walls.
    private static TileRect AxisAlignedLine(Vector2I start, Vector2I end)
    {
        var dx = Math.Abs(end.X - start.X);
        var dy = Math.Abs(end.Y - start.Y);
        if (dx >= dy)
        {
            var minX = Math.Min(start.X, end.X);
            var maxX = Math.Max(start.X, end.X);
            return new TileRect(minX, start.Y, maxX, start.Y);
        }
        var minY = Math.Min(start.Y, end.Y);
        var maxY = Math.Max(start.Y, end.Y);
        return new TileRect(start.X, minY, start.X, maxY);
    }

    private static Vector2I OriginForFootprintCenter(BlueprintDef def, int rotation, Vector2I cursor)
    {
        var (w, h) = (rotation & 1) == 0 ? (def.FootprintW, def.FootprintH) : (def.FootprintH, def.FootprintW);
        return new Vector2I(cursor.X - w / 2, cursor.Y - h / 2);
    }

    private bool IsFootprintInBounds(BlueprintDef def, int rotation, Vector2I origin)
    {
        var (w, h) = (rotation & 1) == 0 ? (def.FootprintW, def.FootprintH) : (def.FootprintH, def.FootprintW);
        if (origin.X < 0 || origin.Y < 0) return false;
        if (origin.X + w > _field.VertWidth - 1) return false;
        if (origin.Y + h > _field.VertHeight - 1) return false;
        return true;
    }

    private bool IsFootprintPlaceable(BlueprintDef def, int rotation, Vector2I origin, int baseLayer)
    {
        if (!IsFootprintInBounds(def, rotation, origin)) return false;
        var (w, h) = (rotation & 1) == 0 ? (def.FootprintW, def.FootprintH) : (def.FootprintH, def.FootprintW);
        if (baseLayer == 0 && !IsFootprintLevel(origin.X, origin.Y, w, h)) return false;
        if (IsFootprintObstructed(origin.X, origin.Y, w, h, baseLayer, def.HeightQuanta)) return false;
        return true;
    }

    // Stack height at the cursor: top of the tallest existing ghost
    // overlapping the footprint. Adds the player's manual offset
    // (Q/E nudges via BuildToolService) so they can lift the ghost
    // off the auto-detected stack base.
    private int ResolveBaseLayer(BlueprintDef def, int rotation, Vector2I origin)
    {
        var (w, h) = (rotation & 1) == 0 ? (def.FootprintW, def.FootprintH) : (def.FootprintH, def.FootprintW);
        var minX = origin.X; var minY = origin.Y;
        var maxX = origin.X + w - 1; var maxY = origin.Y + h - 1;
        var snap = _publisher.Current;
        var top = 0;
        for (var i = 0; i < snap.BlueprintGhosts.Count; i++)
        {
            var g = snap.BlueprintGhosts[i];
            if (!BlueprintCatalog.TryGet(g.DefId, out var od) || od is null) continue;
            var (ow, oh) = (g.Rotation & 1) == 0 ? (od.FootprintW, od.FootprintH) : (od.FootprintH, od.FootprintW);
            var omx = g.OriginTileX + ow - 1;
            var omy = g.OriginTileY + oh - 1;
            if (minX > omx || maxX < g.OriginTileX || minY > omy || maxY < g.OriginTileY) continue;
            var ghostTop = g.BaseLayer + od.HeightQuanta;
            if (ghostTop > top) top = ghostTop;
        }
        return top + _tools.ActiveBuildLayer;
    }

    private bool IsFootprintLevel(int ox, int oy, int w, int h)
    {
        var anchor = _field.Get(ox, oy);
        for (var vy = oy; vy <= oy + h; vy++)
        {
            for (var vx = ox; vx <= ox + w; vx++)
            {
                if (_field.Get(vx, vy) != anchor) return false;
            }
        }
        return true;
    }

    private bool IsFootprintObstructed(int ox, int oy, int w, int h, int baseLayer, int heightQuanta)
    {
        var minX = ox; var minY = oy;
        var maxX = ox + w - 1; var maxY = oy + h - 1;
        var topLayer = baseLayer + heightQuanta;
        var snap = _publisher.Current;
        for (var i = 0; i < snap.BlueprintGhosts.Count; i++)
        {
            var g = snap.BlueprintGhosts[i];
            if (!BlueprintCatalog.TryGet(g.DefId, out var od) || od is null) continue;
            var existingTop = g.BaseLayer + od.HeightQuanta;
            if (baseLayer >= existingTop || topLayer <= g.BaseLayer) continue;
            var (ow, oh) = (g.Rotation & 1) == 0 ? (od.FootprintW, od.FootprintH) : (od.FootprintH, od.FootprintW);
            var omx = g.OriginTileX + ow - 1;
            var omy = g.OriginTileY + oh - 1;
            if (minX <= omx && maxX >= g.OriginTileX && minY <= omy && maxY >= g.OriginTileY) return true;
        }
        for (var i = 0; i < snap.Structures.Count; i++)
        {
            var s = snap.Structures[i];
            if (!BlueprintCatalog.TryGet(s.DefId, out var sd) || sd is null) continue;
            var existingTop = s.BaseLayer + sd.HeightQuanta;
            if (baseLayer >= existingTop || topLayer <= s.BaseLayer) continue;
            var (sw, sh) = (s.Rotation & 1) == 0 ? (sd.FootprintW, sd.FootprintH) : (sd.FootprintH, sd.FootprintW);
            var smx = s.TileX + sw - 1;
            var smy = s.TileY + sh - 1;
            if (minX <= smx && maxX >= s.TileX && minY <= smy && maxY >= s.TileY) return true;
        }
        return false;
    }

    private Vector2I? ProjectMouseToTile(Vector2 mousePos)
    {
        var camera = GetViewport().GetCamera3D();
        if (camera is null) return null;
        var hit = TerrainRayCast.Project(camera, mousePos, _field);
        if (hit is null) return null;
        var tx = (int)Mathf.Floor(hit.Value.X / SimConstants.GodotUnitsPerTile);
        var ty = (int)Mathf.Floor(hit.Value.Z / SimConstants.GodotUnitsPerTile);
        return new Vector2I(tx, ty);
    }

    private static bool IsRotateKey(InputEvent ev)
        => ev is InputEventKey k && k.Pressed && !k.Echo && k.PhysicalKeycode == Key.R;

    private static Color ColorForRectTool(string toolId) => toolId switch
    {
        "zone.stockpile" => new Color(0.85f, 0.65f, 0.35f, 0.30f),
        "zone.farm" => new Color(0.35f, 0.75f, 0.30f, 0.30f),
        "zone.delete" => new Color(0.95f, 0.20f, 0.20f, 0.35f),
        "designate.chop_tree" => new Color(0.95f, 0.25f, 0.20f, 0.30f),
        "designate.mine" => new Color(0.55f, 0.55f, 0.6f, 0.30f),
        "designate.harvest" => new Color(0.95f, 0.85f, 0.30f, 0.30f),
        "designate.cut_plant" => new Color(0.95f, 0.55f, 0.20f, 0.30f),
        "edit.erase" => new Color(0.95f, 0.20f, 0.20f, 0.35f),
        _ => new Color(0.3f, 0.55f, 0.95f, 0.30f),
    };
}
