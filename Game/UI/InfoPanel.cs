using CowColonySim.Game.Input;
using CowColonySim.Sim.Snapshots;
using Godot;

namespace CowColonySim.Game.UI;

// Bottom-left panel showing the currently-selected colonist's id and
// world position. Phase 2 will add needs/jobs.
public partial class InfoPanel : CanvasLayer
{
    private SelectionService _selection = null!;
    private SnapshotPublisher _publisher = null!;
    private Label _label = null!;

    public void Configure(SelectionService selection, SnapshotPublisher publisher)
    {
        _selection = selection;
        _publisher = publisher;
    }

    public override void _Ready()
    {
        Layer = 100;
        var panel = new PanelContainer
        {
            Position = new Vector2(8f, 0f),
            AnchorTop = 1f,
            AnchorBottom = 1f,
            OffsetTop = -120f,
            OffsetBottom = -8f,
            CustomMinimumSize = new Vector2(260f, 110f),
        };
        AddChild(panel);

        _label = new Label
        {
            Text = "no selection",
        };
        _label.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f));
        _label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f));
        _label.AddThemeConstantOverride("outline_size", 4);
        _label.AddThemeFontSizeOverride("font_size", 14);
        panel.AddChild(_label);
    }

    public override void _Process(double delta)
    {
        if (_selection.SelectedEntityId is not int id)
        {
            _label.Text = "no selection\nleft-click colonist to select\nright-click ground to move";
            return;
        }
        var snap = _publisher.Current;
        for (var i = 0; i < snap.Colonists.Count; i++)
        {
            var c = snap.Colonists[i];
            if (c.EntityId != id) continue;
            _label.Text =
                $"colonist #{id}\n" +
                $"pos: ({c.MetersX:F1}m, {c.MetersY:F1}m)\n" +
                $"right-click ground to move";
            return;
        }
        _label.Text = $"colonist #{id} (offline)";
    }
}
