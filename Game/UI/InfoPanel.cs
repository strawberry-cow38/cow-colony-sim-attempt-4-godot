using CowColonySim.Game.Selection;
using CowColonySim.Sim.Commands;
using CowColonySim.Sim.Designations;
using CowColonySim.Sim.Zones;
using CowColonySim.Sim.Snapshots;
using CowColonySim.Sim.World.Components;
using Godot;

namespace CowColonySim.Game.UI;

// Bottom-left panel. Renders different sub-panels per selection kind:
// colonist (needs + job), tree (health + chop actions). Only one
// sub-panel is visible at a time. Buttons submit through CommandBus —
// the panel never mutates the sim directly.
public partial class InfoPanel : CanvasLayer
{
    private SelectionService _selection = null!;
    private SnapshotPublisher _publisher = null!;
    private CommandBus _commands = null!;

    private VBoxContainer _colonistBox = null!;
    private Label _colonistHeader = null!;
    private ProgressBar _hungerBar = null!;
    private ProgressBar _thirstBar = null!;
    private ProgressBar _energyBar = null!;
    private Label _jobLabel = null!;

    private VBoxContainer _treeBox = null!;
    private Label _treeHeader = null!;
    private ProgressBar _treeHealthBar = null!;
    private Button _designateChopBtn = null!;
    private Button _cancelChopBtn = null!;

    private Label _emptyLabel = null!;

    public void Configure(SelectionService selection, SnapshotPublisher publisher, CommandBus commands)
    {
        _selection = selection;
        _publisher = publisher;
        _commands = commands;
    }

    public override void _Ready()
    {
        Layer = 100;
        var panel = new PanelContainer
        {
            Position = new Vector2(8f, 0f),
            AnchorTop = 1f,
            AnchorBottom = 1f,
            OffsetTop = -240f,
            OffsetBottom = -8f,
            CustomMinimumSize = new Vector2(280f, 230f),
        };
        AddChild(panel);

        var root = new VBoxContainer();
        panel.AddChild(root);

        _emptyLabel = MakeLabel("no selection\nleft-click colonist or tree · right-click ground to move");
        root.AddChild(_emptyLabel);

        _colonistBox = new VBoxContainer { Visible = false };
        root.AddChild(_colonistBox);
        _colonistHeader = MakeLabel("colonist");
        _colonistBox.AddChild(_colonistHeader);
        _hungerBar = MakeBar(new Color(0.3f, 0.85f, 0.35f));
        _thirstBar = MakeBar(new Color(0.3f, 0.55f, 0.95f));
        _energyBar = MakeBar(new Color(0.95f, 0.85f, 0.25f));
        _colonistBox.AddChild(MakeLabel("hunger"));
        _colonistBox.AddChild(_hungerBar);
        _colonistBox.AddChild(MakeLabel("thirst"));
        _colonistBox.AddChild(_thirstBar);
        _colonistBox.AddChild(MakeLabel("energy"));
        _colonistBox.AddChild(_energyBar);
        _jobLabel = MakeLabel("job: idle");
        _colonistBox.AddChild(_jobLabel);

        _treeBox = new VBoxContainer { Visible = false };
        root.AddChild(_treeBox);
        _treeHeader = MakeLabel("pine");
        _treeBox.AddChild(_treeHeader);
        _treeBox.AddChild(MakeLabel("health"));
        _treeHealthBar = MakeBar(new Color(0.4f, 0.8f, 0.35f));
        _treeBox.AddChild(_treeHealthBar);
        _designateChopBtn = new Button { Text = "designate chop" };
        _designateChopBtn.Pressed += OnDesignateChop;
        _treeBox.AddChild(_designateChopBtn);
        _cancelChopBtn = new Button { Text = "cancel chop" };
        _cancelChopBtn.Pressed += OnCancelChop;
        _treeBox.AddChild(_cancelChopBtn);
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
        var snap = _publisher.Current;
        if (_selection.SelectedEntityId is int colonistId)
        {
            ShowColonist(snap, colonistId);
            return;
        }
        if (_selection.SelectedTreeId is int treeId)
        {
            ShowTree(snap, treeId);
            return;
        }
        ShowEmpty();
    }

    private void ShowEmpty()
    {
        _emptyLabel.Visible = true;
        _colonistBox.Visible = false;
        _treeBox.Visible = false;
    }

    private void ShowColonist(SimSnapshot snap, int id)
    {
        _emptyLabel.Visible = false;
        _treeBox.Visible = false;
        _colonistBox.Visible = true;
        for (var i = 0; i < snap.Colonists.Count; i++)
        {
            var c = snap.Colonists[i];
            if (c.EntityId != id) continue;
            _colonistHeader.Text =
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
        _colonistHeader.Text = $"colonist #{id} (offline)";
    }

    private void ShowTree(SimSnapshot snap, int id)
    {
        _emptyLabel.Visible = false;
        _colonistBox.Visible = false;
        _treeBox.Visible = true;
        for (var i = 0; i < snap.Trees.Count; i++)
        {
            var t = snap.Trees[i];
            if (t.EntityId != id) continue;
            _treeHeader.Text =
                $"pine #{id}\n" +
                $"tile ({t.TileX}, {t.TileY})";
            _treeHealthBar.MaxValue = 30;
            _treeHealthBar.Value = t.Health;
            var designated = HasChopDesignation(snap, t.TileX, t.TileY);
            _designateChopBtn.Disabled = designated;
            _cancelChopBtn.Disabled = !designated;
            return;
        }
        _treeHeader.Text = $"pine #{id} (felled)";
        _designateChopBtn.Disabled = true;
        _cancelChopBtn.Disabled = true;
    }

    private static bool HasChopDesignation(SimSnapshot snap, int tx, int ty)
    {
        for (var i = 0; i < snap.Designations.Count; i++)
        {
            var d = snap.Designations[i];
            if (d.Kind != DesignationKind.ChopTree) continue;
            if (d.TileX == tx && d.TileY == ty) return true;
        }
        return false;
    }

    private void OnDesignateChop()
    {
        if (_selection.SelectedTreeId is not int id) return;
        var snap = _publisher.Current;
        for (var i = 0; i < snap.Trees.Count; i++)
        {
            var t = snap.Trees[i];
            if (t.EntityId != id) continue;
            if (HasChopDesignation(snap, t.TileX, t.TileY)) return;
            _commands.Submit(new StampDesignationsCommand(
                DesignationKind.ChopTree,
                new TileRect(t.TileX, t.TileY, t.TileX, t.TileY)));
            return;
        }
    }

    private void OnCancelChop()
    {
        if (_selection.SelectedTreeId is not int id) return;
        var snap = _publisher.Current;
        for (var i = 0; i < snap.Trees.Count; i++)
        {
            var t = snap.Trees[i];
            if (t.EntityId != id) continue;
            _commands.Submit(new EraseInRectCommand(
                new TileRect(t.TileX, t.TileY, t.TileX, t.TileY)));
            return;
        }
    }

    private static string KindName(NeedKind kind) => kind switch
    {
        NeedKind.Hunger => "hunger",
        NeedKind.Thirst => "thirst",
        NeedKind.Energy => "energy",
        _ => "?",
    };
}
