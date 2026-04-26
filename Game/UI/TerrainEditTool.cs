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
    private TerrainRenderer _terrain = null!;

    public void Configure(
        BuildToolService tools,
        TerrainEditOverlay overlay,
        Heightfield field,
        TerrainRenderer terrain)
    {
        _tools = tools;
        _overlay = overlay;
        _field = field;
        _terrain = terrain;
    }

    public override void _UnhandledInput(InputEvent ev)
    {
        if (string.IsNullOrEmpty(_tools.ActiveToolId)) return;
        if (ev is not InputEventMouseButton mb || !mb.Pressed) return;
        if (mb.ButtonIndex != MouseButton.Left) return;

        var v = _overlay.SnappedVertex;
        if (v is null) return;

        var delta = _tools.ActiveToolId switch
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

    public override void _Process(double delta)
    {
        if (!_field.HasDirtyRegion) return;
        _field.TryConsumeDirtyRegion(out _, out _, out _, out _);
        // TODO: chunked partial rebuild. For now full rebuild every edit.
        _terrain.Build(_field);
    }
}
