using Godot;

namespace CowColonySim.Game.UI;

// Tracks the currently-selected blueprint / designator from the Build menu.
// Other game systems (terrain editor, blueprint placer) subscribe to
// ToolChanged and react when the active id matches one they own. Empty
// string means "no tool selected" — left-click reverts to plain selection.
//
// ActiveBuildLayer is the Z-stacking offset used for placing blueprint
// ghosts on upper storeys. Layer 0 = on terrain. +1 / -1 nudges via Q/E.
// One layer = one full-wall height (3 m) so a wall-top is exactly one
// layer up.
public partial class BuildToolService : Node
{
    public const int MinBuildLayer = 0;
    public const int MaxBuildLayer = 8;

    public string ActiveToolId { get; private set; } = string.Empty;
    public int ActiveBuildLayer { get; private set; }

    [Signal]
    public delegate void ToolChangedEventHandler(string toolId);

    [Signal]
    public delegate void BuildLayerChangedEventHandler(int layer);

    public void SetActive(string toolId)
    {
        toolId ??= string.Empty;
        if (ActiveToolId == toolId) return;
        ActiveToolId = toolId;
        EmitSignal(SignalName.ToolChanged, toolId);
    }

    public void Clear() => SetActive(string.Empty);

    public void SetBuildLayer(int layer)
    {
        layer = Mathf.Clamp(layer, MinBuildLayer, MaxBuildLayer);
        if (ActiveBuildLayer == layer) return;
        ActiveBuildLayer = layer;
        EmitSignal(SignalName.BuildLayerChanged, layer);
    }

    public override void _UnhandledInput(InputEvent ev)
    {
        if (string.IsNullOrEmpty(ActiveToolId)) return;
        if (ev is not InputEventKey k || !k.Pressed || k.Echo) return;
        if (k.PhysicalKeycode == Key.Q) { SetBuildLayer(ActiveBuildLayer - 1); GetViewport().SetInputAsHandled(); }
        else if (k.PhysicalKeycode == Key.E) { SetBuildLayer(ActiveBuildLayer + 1); GetViewport().SetInputAsHandled(); }
    }
}
