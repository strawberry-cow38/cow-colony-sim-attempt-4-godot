using CowColonySim.Game.Terrain;
using CowColonySim.Sim.Terrain;
using Godot;

namespace CowColonySim.Game.UI;

// Consumes the active build tool + the overlay's snapped vertex and turns
// left-clicks into heightfield edits. After any edit it pulls the dirty
// region out of the field and rebuilds the terrain mesh — currently a
// full rebuild; the next pass switches the renderer to chunked surfaces
// so we only rebuild chunks intersecting the dirty bbox.
public partial class TerrainEditTool : Node
{
    private BuildToolService _tools = null!;
    private TerrainEditOverlay _overlay = null!;
    private Heightfield _field = null!;
    private ChunkedTerrainRenderer _terrain = null!;

    private Vector2I? _flattenStart;
    private short _flattenHeight;

    public void Configure(
        BuildToolService tools,
        TerrainEditOverlay overlay,
        Heightfield field,
        ChunkedTerrainRenderer terrain)
    {
        _tools = tools;
        _overlay = overlay;
        _field = field;
        _terrain = terrain;
    }

    public override void _UnhandledInput(InputEvent ev)
    {
        if (string.IsNullOrEmpty(_tools.ActiveToolId)) return;
        if (ev is not InputEventMouseButton mb) return;
        if (mb.ButtonIndex != MouseButton.Left) return;

        var v = _overlay.SnappedVertex;
        var tool = _tools.ActiveToolId;

        if (tool == "debug_terrain.flatten_rect")
        {
            HandleFlatten(mb.Pressed, v);
            return;
        }

        if (!mb.Pressed) return;
        if (v is null) return;

        var delta = tool switch
        {
            "debug_terrain.raise_vertex" => +1,
            "debug_terrain.lower_vertex" => -1,
            _ => 0,
        };
        if (delta == 0) return;

        var current = _field.Get(v.Value.X, v.Value.Y);
        var next = (short)(current + delta);
        _field.Set(v.Value.X, v.Value.Y, next);
        GetViewport().SetInputAsHandled();
    }

    private void HandleFlatten(bool pressed, Vector2I? snapped)
    {
        if (pressed)
        {
            if (snapped is null) return;
            _flattenStart = snapped;
            _flattenHeight = _field.Get(snapped.Value.X, snapped.Value.Y);
            _overlay.SetRectPreview(snapped, snapped);
            GetViewport().SetInputAsHandled();
            return;
        }

        if (_flattenStart is null) return;
        var start = _flattenStart.Value;
        _flattenStart = null;
        _overlay.SetRectPreview(null, null);

        if (snapped is null) return;
        var end = snapped.Value;
        var minX = Math.Min(start.X, end.X);
        var maxX = Math.Max(start.X, end.X);
        var minY = Math.Min(start.Y, end.Y);
        var maxY = Math.Max(start.Y, end.Y);
        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                _field.Set(x, y, _flattenHeight);
            }
        }
        GetViewport().SetInputAsHandled();
    }

    public override void _Process(double delta)
    {
        if (_flattenStart is not null)
        {
            _overlay.SetRectPreview(_flattenStart, _overlay.SnappedVertex);
        }
        DrainDirtyAndRebuild();
    }

    private void DrainDirtyAndRebuild()
    {
        if (!_field.TryConsumeDirtyRegion(out var minVx, out var minVy, out var maxVx, out var maxVy)) return;
        _terrain.RebuildVertexBbox(minVx, minVy, maxVx, maxVy);
    }

}
