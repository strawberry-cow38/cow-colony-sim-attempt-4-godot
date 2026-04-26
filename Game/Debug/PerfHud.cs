using CowColonySim.Sim;
using Godot;

namespace CowColonySim.Game.Debug;

// Top-left FPS + TPS counter. Always visible.
public partial class PerfHud : CanvasLayer
{
    private const float SampleSeconds = 0.5f;

    private SimRuntime _runtime = null!;
    private Label _label = null!;
    private long _lastTick;
    private float _accum;
    private float _fps;
    private float _tps;

    public void Configure(SimRuntime runtime) => _runtime = runtime;

    public override void _Ready()
    {
        Layer = 100;
        _label = new Label
        {
            Position = new Vector2(8f, 4f),
            Text = "FPS: --   TPS: --",
            ZIndex = 1000,
        };
        _label.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f));
        _label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f));
        _label.AddThemeConstantOverride("outline_size", 4);
        _label.AddThemeFontSizeOverride("font_size", 16);
        AddChild(_label);
        _lastTick = _runtime.TickNumber;
    }

    public override void _Process(double delta)
    {
        _accum += (float)delta;
        if (_accum < SampleSeconds) return;

        var tickNow = _runtime.TickNumber;
        _tps = (tickNow - _lastTick) / _accum;
        _fps = (float)Engine.GetFramesPerSecond();
        _lastTick = tickNow;
        _accum = 0f;

        _label.Text = $"FPS: {_fps:F0}   TPS: {_tps:F0}";
    }
}
