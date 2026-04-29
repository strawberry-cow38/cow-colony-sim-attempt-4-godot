using CowColonySim.Game.Selection;
using CowColonySim.Sim.Blueprints;
using CowColonySim.Sim.Commands;
using CowColonySim.Sim.Snapshots;
using CowColonySim.Sim.World.Components;
using Godot;

namespace CowColonySim.Game.UI;

// Pops up when the player selects a built power Source (test generator).
// Watts slider (0..MaxSupplyW) + on/off toggle + grid status readout.
// Submits SetGeneratorOutputCommand on slider release / toggle press.
public partial class GeneratorPanel : CanvasLayer
{
    private SelectionService _selection = null!;
    private SnapshotPublisher _publisher = null!;
    private CommandBus _commands = null!;

    private Control _root = null!;
    private Label _title = null!;
    private Label _statusLabel = null!;
    private HSlider _slider = null!;
    private Label _wattsLabel = null!;
    private Button _toggle = null!;

    private int? _trackedEntityId;
    private bool _suppressEvents;
    private float _lastPushedWatts = -1f;
    private bool _lastPushedOn;

    public void Configure(SelectionService selection, SnapshotPublisher publisher, CommandBus commands)
    {
        _selection = selection;
        _publisher = publisher;
        _commands = commands;
    }

    public override void _Ready()
    {
        Layer = 25;

        _root = new Control
        {
            AnchorLeft = 1f, AnchorRight = 1f,
            AnchorTop = 0f, AnchorBottom = 0f,
            OffsetLeft = -340f, OffsetRight = -16f,
            OffsetTop = 16f, OffsetBottom = 220f,
            MouseFilter = Control.MouseFilterEnum.Stop,
            Visible = false,
        };
        AddChild(_root);

        var bg = new PanelContainer
        {
            AnchorLeft = 0f, AnchorRight = 1f,
            AnchorTop = 0f, AnchorBottom = 1f,
        };
        bg.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.07f, 0.07f, 0.09f, 0.92f),
            BorderColor = new Color(0.35f, 0.35f, 0.4f),
            BorderWidthTop = 1, BorderWidthBottom = 1, BorderWidthLeft = 1, BorderWidthRight = 1,
            ContentMarginLeft = 12, ContentMarginRight = 12,
            ContentMarginTop = 10, ContentMarginBottom = 10,
        });
        _root.AddChild(bg);

        var col = new VBoxContainer();
        bg.AddChild(col);

        _title = new Label { Text = "Generator" };
        col.AddChild(_title);

        _statusLabel = new Label { Text = "grid: —" };
        col.AddChild(_statusLabel);

        _wattsLabel = new Label { Text = "output: 0 W" };
        col.AddChild(_wattsLabel);

        _slider = new HSlider
        {
            MinValue = 0, MaxValue = 1000, Step = 10, Value = 0,
            CustomMinimumSize = new Vector2(0, 24),
        };
        _slider.ValueChanged += OnSliderChanged;
        _slider.DragEnded += OnSliderDragEnded;
        col.AddChild(_slider);

        _toggle = new Button { Text = "ON", ToggleMode = true };
        _toggle.Pressed += OnTogglePressed;
        col.AddChild(_toggle);

        _selection.SelectionChanged += OnSelectionChanged;
    }

    private void OnSelectionChanged()
    {
        var newId = ResolveGeneratorId();
        _trackedEntityId = newId;
        _root.Visible = newId.HasValue;
        if (newId.HasValue) RefreshFromSnapshot(force: true);
    }

    private int? ResolveGeneratorId()
    {
        if (_selection.SelectedStructureId is not int id) return null;
        var snap = _publisher.Current;
        for (var i = 0; i < snap.Structures.Count; i++)
        {
            if (snap.Structures[i].EntityId != id) continue;
            if (!BlueprintCatalog.TryGet(snap.Structures[i].DefId, out var def) || def is null) return null;
            if (def.Power == PowerNodeKind.Source) return id;
            return null;
        }
        return null;
    }

    public override void _Process(double delta)
    {
        if (_trackedEntityId is null) return;
        RefreshFromSnapshot(force: false);
    }

    private void RefreshFromSnapshot(bool force)
    {
        if (_trackedEntityId is not int id) return;
        var snap = _publisher.Current;
        PowerNodeView? node = null;
        for (var i = 0; i < snap.PowerNodes.Count; i++)
        {
            if (snap.PowerNodes[i].EntityId == id) { node = snap.PowerNodes[i]; break; }
        }
        if (node is null) { _root.Visible = false; return; }
        var n = node.Value;
        // Resolve the def to get the slider cap.
        StructureView? sv = null;
        for (var i = 0; i < snap.Structures.Count; i++)
        {
            if (snap.Structures[i].EntityId == id) { sv = snap.Structures[i]; break; }
        }
        var maxW = 1000f;
        if (sv is not null && BlueprintCatalog.TryGet(sv.Value.DefId, out var def) && def is not null && def.MaxSupplyW > 0f)
        {
            maxW = def.MaxSupplyW;
            _title.Text = def.DisplayName;
        }
        if (force || _slider.MaxValue != maxW)
        {
            _suppressEvents = true;
            _slider.MaxValue = maxW;
            _slider.Value = n.SupplyW;
            _suppressEvents = false;
        }

        _wattsLabel.Text = $"output: {n.SupplyW:F0} / {maxW:F0} W";
        _toggle.SetPressedNoSignal(n.IsActive);
        _toggle.Text = n.IsActive ? "ON" : "OFF";

        var status = "—";
        if (n.GridId >= 0)
        {
            for (var i = 0; i < snap.PowerGrids.Count; i++)
            {
                if (snap.PowerGrids[i].Id != n.GridId) continue;
                var g = snap.PowerGrids[i];
                status = $"grid #{g.Id} {g.Status}\nsupply {g.TotalSupplyW:F0} W / demand {g.TotalDemandW:F0} W\npylons {g.PylonCount} • sinks {g.SinkCount}";
                break;
            }
        }
        else
        {
            status = "no pylon in range (place one within 6 tiles)";
        }
        _statusLabel.Text = status;
    }

    private void OnSliderChanged(double value)
    {
        if (_suppressEvents || _trackedEntityId is null) return;
        _wattsLabel.Text = $"output: {value:F0} W";
    }

    private void OnSliderDragEnded(bool changed)
    {
        if (!changed || _trackedEntityId is null) return;
        Push((float)_slider.Value, _toggle.ButtonPressed);
    }

    private void OnTogglePressed()
    {
        if (_trackedEntityId is null) return;
        _toggle.Text = _toggle.ButtonPressed ? "ON" : "OFF";
        Push((float)_slider.Value, _toggle.ButtonPressed);
    }

    private void Push(float watts, bool isOn)
    {
        if (_trackedEntityId is not int id) return;
        if (Mathf.IsEqualApprox(watts, _lastPushedWatts) && isOn == _lastPushedOn) return;
        _lastPushedWatts = watts;
        _lastPushedOn = isOn;
        _commands.Submit(new SetGeneratorOutputCommand(id, watts, isOn));
    }
}
