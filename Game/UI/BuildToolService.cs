using Godot;

namespace CowColonySim.Game.UI;

// Tracks the currently-selected blueprint / designator from the Build menu.
// Other game systems (terrain editor, blueprint placer) subscribe to
// ToolChanged and react when the active id matches one they own. Empty
// string means "no tool selected" — left-click reverts to plain selection.
//
// ActiveBuildLayer is a manual offset (in 0.75 m quanta) the player
// stacks on top of the auto-detected build base. PlacementTool reads
// the topmost ghost overlapping the cursor footprint and uses
// (autoBase + ActiveBuildLayer) as the placement layer — so just
// clicking on a wall-top stacks the next blueprint on the floor above
// without touching Q/E. Q/E nudges ±1 quantum (= 1 quarter wall) for
// when the player wants to lift the ghost off the auto stack.
public partial class BuildToolService : Node
{
    public const int MinBuildLayer = 0;
    public const int MaxBuildLayer = 32;

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
