using CowColonySim.Sim;
using CowColonySim.Sim.Time;
using Godot;

namespace CowColonySim.Game.Time;

// Top-right HUD showing in-game date + clock + active speed multiplier.
// Also owns the speed/pause hotkeys:
//   space    → toggle pause
//   1/2/3/4  → 1× / 2× / 3× / 6×
public partial class TimeHud : CanvasLayer
{
    private SimRuntime _runtime = null!;
    private Label _label = null!;
    private int _lastNonZeroSpeed = 1;

    public void Configure(SimRuntime runtime) => _runtime = runtime;

    public override void _Ready()
    {
        Layer = 100;
        var panel = new PanelContainer
        {
            AnchorLeft = 1f,
            AnchorRight = 1f,
            AnchorTop = 0f,
            OffsetLeft = -260f,
            OffsetRight = -8f,
            OffsetTop = 8f,
            OffsetBottom = 70f,
        };
        AddChild(panel);

        _label = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            Text = "—",
        };
        _label.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f));
        _label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f));
        _label.AddThemeConstantOverride("outline_size", 4);
        _label.AddThemeFontSizeOverride("font_size", 14);
        panel.AddChild(_label);
    }

    public override void _UnhandledInput(InputEvent ev)
    {
        if (ev is not InputEventKey key || !key.Pressed || key.Echo) return;
        switch (key.PhysicalKeycode)
        {
            case Key.Space:
                if (_runtime.Speed > 0)
                {
                    _lastNonZeroSpeed = _runtime.Speed;
                    _runtime.Speed = 0;
                }
                else
                {
                    _runtime.Speed = _lastNonZeroSpeed;
                }
                GetViewport().SetInputAsHandled();
                break;
            case Key.Key1: _runtime.Speed = 1; _lastNonZeroSpeed = 1; GetViewport().SetInputAsHandled(); break;
            case Key.Key2: _runtime.Speed = 2; _lastNonZeroSpeed = 2; GetViewport().SetInputAsHandled(); break;
            case Key.Key3: _runtime.Speed = 3; _lastNonZeroSpeed = 3; GetViewport().SetInputAsHandled(); break;
            case Key.Key4: _runtime.Speed = 6; _lastNonZeroSpeed = 6; GetViewport().SetInputAsHandled(); break;
        }
    }

    public override void _Process(double delta)
    {
        var dt = GameClock.DateTimeAt(_runtime.TickNumber);
        var speed = _runtime.Speed;
        var speedTag = speed == 0 ? "PAUSED" : $"{speed}×";
        _label.Text =
            $"{dt:yyyy-MM-dd}\n" +
            $"{dt:HH:mm:ss}\n" +
            $"{speedTag}  [space pause · 1/2/3/4 = 1/2/3/6×]";
    }
}
