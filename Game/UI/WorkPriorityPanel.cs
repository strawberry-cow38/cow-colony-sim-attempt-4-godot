using CowColonySim.Sim.Commands;
using CowColonySim.Sim.Designations;
using CowColonySim.Sim.Snapshots;
using Godot;

namespace CowColonySim.Game.UI;

// Per-colonist x per-WorkType priority grid. Hidden by default; press P
// to toggle. Two display modes:
//   * Numbers: each cell shows 1-8 or blank. Click cycles
//     blank->1->2->...->8->blank (left), reverse on right-click.
//     Blank == 0 == "won't do this work".
//   * Checks: each cell is a checkbox. Checked sets priority to
//     DefaultPriority (4); unchecked sets 0. Toggling between modes
//     never destroys the underlying byte — Numbers mode shows the
//     stored value, Checks mode shows nonzero-vs-zero.
public partial class WorkPriorityPanel : CanvasLayer
{
    private const int RowHeight = 30;
    private const int ColWidth = 56;
    private const int NameColWidth = 110;
    private const int HeaderHeight = 34;
    private const int Padding = 12;

    private static readonly string[] PlaceholderNames =
    {
        "Aki", "Bex", "Cal", "Dro", "Ena", "Fen", "Gus", "Hao",
        "Iri", "Jun", "Kio", "Lev", "Mio", "Nyx", "Ona", "Pip",
    };

    private SnapshotPublisher _publisher = null!;
    private CommandBus _commands = null!;

    private Control _root = null!;
    private GridContainer _grid = null!;
    private Button _modeButton = null!;
    private bool _useChecksMode;
    private int _lastColonistRosterHash;

    private readonly List<Row> _rows = new();

    private sealed class Row
    {
        public int EntityId;
        public Label Name = null!;
        public Cell[] Cells = null!;
    }

    private sealed class Cell
    {
        public Button Button = null!;
        public Label Number = null!;
        public StyleBoxFlat Style = null!;
        public byte Shown;
    }

    public void Configure(SnapshotPublisher publisher, CommandBus commands)
    {
        _publisher = publisher;
        _commands = commands;
    }

    public override void _Ready()
    {
        Layer = 30;
        Visible = false;

        _root = new Control
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f,
            AnchorTop = 1f, AnchorBottom = 1f,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        AddChild(_root);

        var bg = new PanelContainer
        {
            AnchorLeft = 0f, AnchorRight = 1f,
            AnchorTop = 0f, AnchorBottom = 1f,
        };
        var bgStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.07f, 0.07f, 0.09f, 0.92f),
            BorderColor = new Color(0.35f, 0.35f, 0.4f),
            BorderWidthTop = 1, BorderWidthBottom = 1, BorderWidthLeft = 1, BorderWidthRight = 1,
            ContentMarginLeft = Padding, ContentMarginRight = Padding,
            ContentMarginTop = Padding, ContentMarginBottom = Padding,
        };
        bg.AddThemeStyleboxOverride("panel", bgStyle);
        _root.AddChild(bg);

        var col = new VBoxContainer { CustomMinimumSize = new Vector2(NameColWidth + ColWidth * WorkTypes.Count, 0) };
        bg.AddChild(col);

        var headerRow = new HBoxContainer { CustomMinimumSize = new Vector2(0, HeaderHeight) };
        col.AddChild(headerRow);

        _modeButton = new Button
        {
            Text = "Numbers",
            CustomMinimumSize = new Vector2(NameColWidth, HeaderHeight),
            FocusMode = Control.FocusModeEnum.None,
        };
        _modeButton.Pressed += OnModeTogglePressed;
        headerRow.AddChild(_modeButton);

        for (var i = 0; i < WorkTypes.Count; i++)
        {
            var lbl = new Label
            {
                Text = WorkTypes.DisplayName((WorkType)i),
                CustomMinimumSize = new Vector2(ColWidth, HeaderHeight),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            headerRow.AddChild(lbl);
        }

        _grid = new GridContainer { Columns = WorkTypes.Count + 1 };
        col.AddChild(_grid);
    }

    public override void _UnhandledInput(InputEvent ev)
    {
        if (ev is not InputEventKey k || !k.Pressed || k.Echo) return;
        if (k.PhysicalKeycode == Key.P && !k.CtrlPressed && !k.AltPressed && !k.ShiftPressed)
        {
            Visible = !Visible;
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Process(double delta)
    {
        if (!Visible) return;
        var snap = _publisher.Current;
        var roster = HashRoster(snap);
        if (roster != _lastColonistRosterHash)
        {
            RebuildRows(snap);
            _lastColonistRosterHash = roster;
        }
        UpdateRowValues(snap);
    }

    private static int HashRoster(SimSnapshot snap)
    {
        var h = 17;
        foreach (var c in snap.Colonists) h = unchecked(h * 31 + c.EntityId);
        return h;
    }

    private void RebuildRows(SimSnapshot snap)
    {
        foreach (var child in _grid.GetChildren()) child.QueueFree();
        _rows.Clear();

        for (var i = 0; i < snap.Colonists.Count; i++)
        {
            var c = snap.Colonists[i];
            var row = new Row { EntityId = c.EntityId, Cells = new Cell[WorkTypes.Count] };

            row.Name = new Label
            {
                Text = NameFor(c.EntityId),
                CustomMinimumSize = new Vector2(NameColWidth, RowHeight),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
            };
            _grid.AddChild(row.Name);

            for (var w = 0; w < WorkTypes.Count; w++)
            {
                var capturedId = c.EntityId;
                var capturedW = (WorkType)w;
                var cell = new Cell();

                cell.Button = new Button
                {
                    CustomMinimumSize = new Vector2(ColWidth, RowHeight),
                    FocusMode = Control.FocusModeEnum.None,
                    ToggleMode = false,
                    Text = string.Empty,
                };
                cell.Style = new StyleBoxFlat
                {
                    BgColor = new Color(0.18f, 0.18f, 0.2f),
                    BorderColor = new Color(0.35f, 0.35f, 0.4f),
                    BorderWidthTop = 1, BorderWidthBottom = 1,
                    BorderWidthLeft = 1, BorderWidthRight = 1,
                };
                cell.Button.AddThemeStyleboxOverride("normal", cell.Style);
                cell.Button.AddThemeStyleboxOverride("hover", cell.Style);
                cell.Button.AddThemeStyleboxOverride("pressed", cell.Style);
                cell.Button.AddThemeStyleboxOverride("focus", cell.Style);

                cell.Number = new Label
                {
                    Text = string.Empty,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    AnchorLeft = 0f, AnchorRight = 1f,
                    AnchorTop = 0f, AnchorBottom = 1f,
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                };
                cell.Button.AddChild(cell.Number);

                cell.Button.GuiInput += @event => OnCellInput(@event, capturedId, capturedW);

                _grid.AddChild(cell.Button);
                row.Cells[w] = cell;
            }
            _rows.Add(row);
        }
    }

    private void UpdateRowValues(SimSnapshot snap)
    {
        for (var i = 0; i < _rows.Count && i < snap.Colonists.Count; i++)
        {
            var row = _rows[i];
            var c = snap.Colonists[i];
            if (row.EntityId != c.EntityId) continue;
            var prios = c.WorkPriorities;
            if (prios is null) continue;
            for (var w = 0; w < WorkTypes.Count && w < prios.Length; w++)
            {
                var cell = row.Cells[w];
                var v = prios[w];
                if (cell.Shown == v) continue;
                cell.Shown = v;
                ApplyCellVisual(cell, v);
            }
        }
    }

    private void ApplyCellVisual(Cell cell, byte v)
    {
        if (_useChecksMode)
        {
            cell.Number.Text = v > 0 ? "✓" : string.Empty;
            cell.Style.BgColor = v > 0 ? new Color(0.18f, 0.36f, 0.22f) : new Color(0.22f, 0.16f, 0.16f);
        }
        else
        {
            cell.Number.Text = v == 0 ? string.Empty : v.ToString();
            cell.Style.BgColor = v == 0 ? new Color(0.22f, 0.16f, 0.16f) : ColorForPriority(v);
        }
    }

    private static Color ColorForPriority(byte v)
    {
        // 1 = bright green (best), 8 = dim red. Linear hue ramp.
        var t = Mathf.Clamp((v - 1) / 7f, 0f, 1f);
        var r = 0.2f + 0.55f * t;
        var g = 0.55f - 0.35f * t;
        var b = 0.2f;
        return new Color(r, g, b);
    }

    private void OnCellInput(InputEvent ev, int colonistId, WorkType type)
    {
        if (ev is not InputEventMouseButton mb || !mb.Pressed) return;
        var current = ReadCurrent(colonistId, type);
        byte next = current;
        if (_useChecksMode)
        {
            next = (byte)(current > 0 ? 0 : Sim.World.Components.WorkPriorities.DefaultPriority);
        }
        else if (mb.ButtonIndex == MouseButton.Left)
        {
            // 0 -> 1 -> 2 -> ... -> 8 -> 0
            next = (byte)((current + 1) % (Sim.World.Components.WorkPriorities.MaxPriority + 1));
        }
        else if (mb.ButtonIndex == MouseButton.Right)
        {
            // 0 -> 8 -> 7 -> ... -> 1 -> 0
            next = current == 0
                ? Sim.World.Components.WorkPriorities.MaxPriority
                : (byte)(current - 1);
        }
        else
        {
            return;
        }

        _commands.Submit(new SetWorkPriorityCommand(colonistId, type, next));
    }

    private byte ReadCurrent(int colonistId, WorkType type)
    {
        var snap = _publisher.Current;
        for (var i = 0; i < snap.Colonists.Count; i++)
        {
            if (snap.Colonists[i].EntityId != colonistId) continue;
            var p = snap.Colonists[i].WorkPriorities;
            if (p is null || (int)type >= p.Length) return 0;
            return p[(int)type];
        }
        return 0;
    }

    private void OnModeTogglePressed()
    {
        _useChecksMode = !_useChecksMode;
        _modeButton.Text = _useChecksMode ? "Checks" : "Numbers";
        // Force visual refresh of every cell on next frame.
        for (var i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            for (var w = 0; w < row.Cells.Length; w++)
            {
                row.Cells[w].Shown = byte.MaxValue; // sentinel
            }
        }
    }

    private static string NameFor(int id)
    {
        if (id <= 0) return "?";
        return PlaceholderNames[(uint)id % PlaceholderNames.Length];
    }
}
