using CowColonySim.Game.Terrain;
using CowColonySim.Sim;
using CowColonySim.Sim.Blueprints;
using CowColonySim.Sim.Commands;
using CowColonySim.Sim.Designations;
using CowColonySim.Sim.Terrain;
using CowColonySim.Sim.Zones;
using Godot;

namespace CowColonySim.Game.UI;

// Owns left-click placement for zones, designators, and blueprints.
// Reads BuildToolService.ActiveToolId + BlueprintCatalog to decide
// which mode to run. Drives RectDragOverlay (drag-rect modes) and
// BlueprintGhostPreview (Footprint/Single blueprint mode).
//
// Tool id conventions:
//   zone.<type>          -> drag rect, CreateZoneCommand
//   designate.<kind>     -> drag rect, StampDesignationsCommand
//   blueprint.<defId>    -> Single / LineDrag / Footprint placement
//                           (mode resolved via BlueprintCatalog)
public partial class PlacementTool : Node
{
    private BuildToolService _tools = null!;
    private RectDragOverlay _rectOverlay = null!;
    private BlueprintGhostPreview _ghostPreview = null!;
    private Heightfield _field = null!;
    private CommandBus _commands = null!;

    private Vector2I? _dragStart;
    private int _blueprintRotation;

    public void Configure(
        BuildToolService tools,
        RectDragOverlay rectOverlay,
        BlueprintGhostPreview ghostPreview,
        Heightfield field,
        CommandBus commands)
    {
        _tools = tools;
        _rectOverlay = rectOverlay;
        _ghostPreview = ghostPreview;
        _field = field;
        _commands = commands;
    }

    public override void _Ready()
    {
        _tools.ToolChanged += OnToolChanged;
    }

    private void OnToolChanged(string toolId)
    {
        _dragStart = null;
        _rectOverlay.PreviewRect = null;
        _ghostPreview.DefId = null;
        _blueprintRotation = 0;
    }

    public override void _UnhandledInput(InputEvent ev)
    {
        var toolId = _tools.ActiveToolId;
        if (string.IsNullOrEmpty(toolId)) return;

        if (toolId.StartsWith("blueprint.") && ev is InputEventKey k && k.Pressed && !k.Echo
            && k.PhysicalKeycode == Key.R)
        {
            _blueprintRotation = (_blueprintRotation + 1) & 3;
            GetViewport().SetInputAsHandled();
            return;
        }

        if (ev is not InputEventMouseButton mb) return;
        if (mb.ButtonIndex != MouseButton.Left) return;

        var tile = ProjectMouseToTile(mb.Position);
        if (tile is null) return;

        if (toolId.StartsWith("zone.") || toolId.StartsWith("designate."))
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

        if (toolId.StartsWith("zone.") || toolId.StartsWith("designate."))
        {
            _ghostPreview.DefId = null;
            if (_dragStart is not null && hovered is not null)
            {
                _rectOverlay.QuadColor = ColorForRectTool(toolId);
                _rectOverlay.PreviewRect = TileRect.FromCorners(
                    _dragStart.Value.X, _dragStart.Value.Y, hovered.Value.X, hovered.Value.Y);
            }
            else if (hovered is not null)
            {
                _rectOverlay.QuadColor = ColorForRectTool(toolId);
                _rectOverlay.PreviewRect = new TileRect(hovered.Value.X, hovered.Value.Y, hovered.Value.X, hovered.Value.Y);
            }
            else
            {
                _rectOverlay.PreviewRect = null;
            }
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
            _ghostPreview.DefId = def.Id;
            _ghostPreview.OriginTileX = origin.X;
            _ghostPreview.OriginTileY = origin.Y;
            _ghostPreview.RotationSteps = _blueprintRotation;
            _ghostPreview.Valid = IsFootprintInBounds(def, _blueprintRotation, origin);
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
        var rect = TileRect.FromCorners(_dragStart.Value.X, _dragStart.Value.Y, tile.X, tile.Y);
        _dragStart = null;
        _rectOverlay.PreviewRect = null;

        if (toolId.StartsWith("zone."))
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
                _ => (DesignationKind?)null,
            };
            if (kind is null) return;
            _commands.Submit(new StampDesignationsCommand(kind.Value, rect));
        }
    }

    private void HandleBlueprintClick(Vector2I tile, string toolId)
    {
        var defId = toolId.Substring("blueprint.".Length);
        if (!BlueprintCatalog.TryGet(defId, out var def) || def is null) return;

        var origin = OriginForFootprintCenter(def, _blueprintRotation, tile);
        if (!IsFootprintInBounds(def, _blueprintRotation, origin)) return;

        _commands.Submit(new PlaceBlueprintGhostCommand(def.Id, origin.X, origin.Y, _blueprintRotation));
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

    private static Color ColorForRectTool(string toolId) => toolId switch
    {
        "zone.stockpile" => new Color(0.85f, 0.65f, 0.35f, 0.30f),
        "zone.farm" => new Color(0.35f, 0.75f, 0.30f, 0.30f),
        "designate.chop_tree" => new Color(0.95f, 0.25f, 0.20f, 0.30f),
        "designate.mine" => new Color(0.55f, 0.55f, 0.6f, 0.30f),
        "designate.harvest" => new Color(0.95f, 0.85f, 0.30f, 0.30f),
        _ => new Color(1f, 0.85f, 0.25f, 0.30f),
    };
}
