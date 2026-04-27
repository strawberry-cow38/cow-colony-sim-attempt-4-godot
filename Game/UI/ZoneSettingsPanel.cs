using CowColonySim.Game.Selection;
using CowColonySim.Sim.Commands;
using CowColonySim.Sim.Pathfinding;
using CowColonySim.Sim.Snapshots;
using CowColonySim.Sim.Zones;
using Godot;

namespace CowColonySim.Game.UI;

// Bottom-right panel: shows fields of the currently-selected zone and
// pushes SetZoneSettingsCommand on Apply. Reads ZoneView from the
// snapshot (no ECS access). Per-type rows hide when not relevant.
public partial class ZoneSettingsPanel : CanvasLayer
{
    private SelectionService _selection = null!;
    private SnapshotPublisher _publisher = null!;
    private CommandBus _commands = null!;

    private PanelContainer _root = null!;
    private Label _header = null!;
    private LineEdit _nameEdit = null!;
    private HBoxContainer _priorityRow = null!;
    private SpinBox _priority = null!;
    private HBoxContainer _cropRow = null!;
    private SpinBox _cropDefId = null!;
    private CheckBox _allowSowing = null!;
    private CheckBox _allowHarvest = null!;
    private Button _apply = null!;
    private Button _delete = null!;
    private TileRect _boundRect;

    private int _boundZoneId = -1;
    private bool _userEdited;

    public void Configure(SelectionService selection, SnapshotPublisher publisher, CommandBus commands)
    {
        _selection = selection;
        _publisher = publisher;
        _commands = commands;
    }

    public override void _Ready()
    {
        Layer = 100;

        _root = new PanelContainer
        {
            AnchorLeft = 1f, AnchorRight = 1f,
            AnchorTop = 1f, AnchorBottom = 1f,
            OffsetLeft = -300f, OffsetRight = -8f,
            OffsetTop = -200f, OffsetBottom = -8f,
            CustomMinimumSize = new Vector2(280f, 190f),
            Visible = false,
        };
        AddChild(_root);

        var box = new VBoxContainer();
        _root.AddChild(box);

        _header = MakeLabel("zone");
        box.AddChild(_header);

        box.AddChild(MakeLabel("name"));
        _nameEdit = new LineEdit { CustomMinimumSize = new Vector2(0f, 24f) };
        _nameEdit.TextChanged += _ => _userEdited = true;
        box.AddChild(_nameEdit);

        _priorityRow = new HBoxContainer();
        _priorityRow.AddChild(MakeLabel("priority"));
        _priority = new SpinBox { MinValue = 0, MaxValue = 9, Step = 1 };
        _priority.ValueChanged += _ => _userEdited = true;
        _priorityRow.AddChild(_priority);
        box.AddChild(_priorityRow);

        _cropRow = new HBoxContainer();
        _cropRow.AddChild(MakeLabel("crop id"));
        _cropDefId = new SpinBox { MinValue = 0, MaxValue = 999, Step = 1 };
        _cropDefId.ValueChanged += _ => _userEdited = true;
        _cropRow.AddChild(_cropDefId);
        box.AddChild(_cropRow);

        _allowSowing = new CheckBox { Text = "allow sowing" };
        _allowSowing.Toggled += _ => _userEdited = true;
        box.AddChild(_allowSowing);

        _allowHarvest = new CheckBox { Text = "allow harvest" };
        _allowHarvest.Toggled += _ => _userEdited = true;
        box.AddChild(_allowHarvest);

        _apply = new Button { Text = "Apply" };
        _apply.Pressed += OnApply;
        box.AddChild(_apply);

        _delete = new Button { Text = "Delete zone" };
        _delete.Pressed += OnDelete;
        box.AddChild(_delete);
    }

    public override void _Process(double delta)
    {
        var zoneId = _selection.SelectedZoneId;
        if (zoneId is null)
        {
            _root.Visible = false;
            _boundZoneId = -1;
            return;
        }

        var snap = _publisher.Current;
        ZoneView? found = null;
        for (var i = 0; i < snap.Zones.Count; i++)
        {
            if (snap.Zones[i].ZoneId == zoneId.Value)
            {
                found = snap.Zones[i];
                break;
            }
        }
        if (found is null)
        {
            _root.Visible = false;
            _boundZoneId = -1;
            return;
        }

        var z = found.Value;
        _root.Visible = true;
        if (_boundZoneId != z.ZoneId || !_userEdited)
        {
            _header.Text = $"{z.Type} #{z.ZoneId} ({z.TileCount} tiles)";
            _nameEdit.Text = z.Name;
            _priority.Value = z.Priority;
            _cropDefId.Value = z.CropDefId;
            _allowSowing.ButtonPressed = z.AllowSowing;
            _allowHarvest.ButtonPressed = z.AllowHarvest;
            _priorityRow.Visible = z.Type == ZoneType.Stockpile;
            _cropRow.Visible = z.Type == ZoneType.Farm;
            _allowSowing.Visible = z.Type == ZoneType.Farm;
            _allowHarvest.Visible = z.Type == ZoneType.Farm;
            _boundZoneId = z.ZoneId;
            _boundRect = new TileRect(z.MinTileX, z.MinTileY, z.MaxTileX, z.MaxTileY);
            _userEdited = false;
        }
        else
        {
            _boundRect = new TileRect(z.MinTileX, z.MinTileY, z.MaxTileX, z.MaxTileY);
        }
    }

    private void OnApply()
    {
        if (_boundZoneId < 0) return;
        _commands.Submit(new SetZoneSettingsCommand(
            _boundZoneId,
            _nameEdit.Text,
            (int)_priority.Value,
            (int)_cropDefId.Value,
            _allowSowing.ButtonPressed,
            _allowHarvest.ButtonPressed));
        _userEdited = false;
    }

    private void OnDelete()
    {
        if (_boundZoneId < 0) return;
        _commands.Submit(new EraseInRectCommand(_boundRect));
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
}
