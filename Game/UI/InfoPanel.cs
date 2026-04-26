using CowColonySim.Game.Selection;
using CowColonySim.Sim.Snapshots;
using CowColonySim.Sim.World.Components;
using Godot;

namespace CowColonySim.Game.UI;

// Bottom-left panel showing the currently-selected colonist's id, position,
// needs (hunger/thirst/energy bars), and active job.
public partial class InfoPanel : CanvasLayer
{
    private SelectionService _selection = null!;
    private SnapshotPublisher _publisher = null!;
    private Label _header = null!;
    private ProgressBar _hungerBar = null!;
    private ProgressBar _thirstBar = null!;
    private ProgressBar _energyBar = null!;
    private Label _jobLabel = null!;

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
            OffsetTop = -200f,
            OffsetBottom = -8f,
            CustomMinimumSize = new Vector2(280f, 190f),
        };
        AddChild(panel);

        var box = new VBoxContainer();
        panel.AddChild(box);

        _header = MakeLabel("no selection");
        box.AddChild(_header);

        _hungerBar = MakeBar(new Color(0.3f, 0.85f, 0.35f));
        _thirstBar = MakeBar(new Color(0.3f, 0.55f, 0.95f));
        _energyBar = MakeBar(new Color(0.95f, 0.85f, 0.25f));
        box.AddChild(MakeLabel("hunger"));
        box.AddChild(_hungerBar);
        box.AddChild(MakeLabel("thirst"));
        box.AddChild(_thirstBar);
        box.AddChild(MakeLabel("energy"));
        box.AddChild(_energyBar);

        _jobLabel = MakeLabel("job: idle");
        box.AddChild(_jobLabel);
    }

    private static Label MakeLabel(string text)
    {
        var l = new Label { Text = text };
        l.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f));
        l.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f));
        l.AddThemeConstantOverride("outline_size", 4);
        l.AddThemeFontSizeOverride("font_size", 13);
        return l;
    }

    private static ProgressBar MakeBar(Color fill)
    {
        var bar = new ProgressBar
        {
            MinValue = 0,
            MaxValue = 100,
            Value = 100,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(0f, 14f),
        };
        var fillStyle = new StyleBoxFlat { BgColor = fill };
        var bgStyle = new StyleBoxFlat { BgColor = new Color(0.15f, 0.15f, 0.18f) };
        bar.AddThemeStyleboxOverride("fill", fillStyle);
        bar.AddThemeStyleboxOverride("background", bgStyle);
        return bar;
    }

    public override void _Process(double delta)
    {
        if (_selection.SelectedEntityId is not int id)
        {
            _header.Text = "no selection\nleft-click colonist · right-click ground to move";
            _hungerBar.Value = 0;
            _thirstBar.Value = 0;
            _energyBar.Value = 0;
            _jobLabel.Text = "job: —";
            return;
        }

        var snap = _publisher.Current;
        for (var i = 0; i < snap.Colonists.Count; i++)
        {
            var c = snap.Colonists[i];
            if (c.EntityId != id) continue;
            _header.Text =
                $"colonist #{id}\n" +
                $"pos: ({c.MetersX:F1}m, {c.MetersY:F1}m)";
            _hungerBar.Value = c.Hunger;
            _thirstBar.Value = c.Thirst;
            _energyBar.Value = c.Energy;
            _jobLabel.Text = c.JobActive
                ? $"job: satisfy {KindName(c.JobKind)}"
                : "job: idle";
            return;
        }
        _header.Text = $"colonist #{id} (offline)";
    }

    private static string KindName(NeedKind kind) => kind switch
    {
        NeedKind.Hunger => "hunger",
        NeedKind.Thirst => "thirst",
        NeedKind.Energy => "energy",
        _ => "?",
    };
}
