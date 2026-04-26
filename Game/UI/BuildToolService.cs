using Godot;

namespace CowColonySim.Game.UI;

// Tracks the currently-selected blueprint / designator from the Build menu.
// Other game systems (terrain editor, blueprint placer) subscribe to
// ToolChanged and react when the active id matches one they own. Empty
// string means "no tool selected" — left-click reverts to plain selection.
public partial class BuildToolService : Node
{
    public string ActiveToolId { get; private set; } = string.Empty;

    [Signal]
    public delegate void ToolChangedEventHandler(string toolId);

    public void SetActive(string toolId)
    {
        toolId ??= string.Empty;
        if (ActiveToolId == toolId) return;
        ActiveToolId = toolId;
        EmitSignal(SignalName.ToolChanged, toolId);
    }

    public void Clear() => SetActive(string.Empty);
}
