using System.Linq;
using System.Text;
using CowColonySim.Game.Camera;
using CowColonySim.Sim;
using Godot;

namespace CowColonySim.Game.Debug;

// Top-left perf overlay. Always-on summary line; F3 toggles a detail panel
// with render counters and per-system sim tick timings. Avg = EWMA over
// the last ~16 ticks; Max accumulates until reset (right-arrow resets).
public partial class PerfHud : CanvasLayer
{
    private const float SampleSeconds = 0.5f;

    private SimRuntime _runtime = null!;
    private CameraRig? _rig;
    private Label _summary = null!;
    private Label _detail = null!;
    private long _lastTick;
    private float _accum;
    private float _fps;
    private float _tps;
    private bool _detailVisible;

    public void Configure(SimRuntime runtime, CameraRig? rig = null)
    {
        _runtime = runtime;
        _rig = rig;
    }

    public override void _Ready()
    {
        Layer = 100;

        _summary = MakeLabel(new Vector2(8f, 4f), 16);
        _summary.Text = "FPS: --   TPS: --";
        AddChild(_summary);

        _detail = MakeLabel(new Vector2(8f, 28f), 13);
        _detail.Text = string.Empty;
        _detail.Visible = false;
        AddChild(_detail);

        _lastTick = _runtime.TickNumber;
    }

    public override void _UnhandledInput(InputEvent ev)
    {
        if (ev is not InputEventKey key || !key.Pressed || key.Echo) return;
        switch (key.PhysicalKeycode)
        {
            case Key.F3:
                _detailVisible = !_detailVisible;
                _detail.Visible = _detailVisible;
                GetViewport().SetInputAsHandled();
                break;
            case Key.F4:
                foreach (var s in _runtime.Scheduler.Metrics.Systems.Values) s.ResetMax();
                GetViewport().SetInputAsHandled();
                break;
        }
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

        var zoomTxt = _rig is null
            ? string.Empty
            : $"   ZOOM: {_rig.ZoomPercent:F1}% ({_rig.CurrentDistance:F0}u)";
        var sun = _runtime.Publisher.Current.Lighting.SunFraction;
        _summary.Text = $"FPS: {_fps:F0}   TPS: {_tps:F0}{zoomTxt}   SUN: {sun * 100f:F0}%   [F3 detail · F4 reset max]";

        if (!_detailVisible) return;

        var sb = new StringBuilder(512);
        AppendRender(sb);
        sb.Append('\n');
        AppendSim(sb);
        _detail.Text = sb.ToString();
    }

    private static void AppendRender(StringBuilder sb)
    {
        var procMs = (double)Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0;
        var physMs = (double)Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000.0;
        var draws = (long)Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);
        var prims = (long)Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame);
        var objs = (long)Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame);
        var vmem = (double)Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed);
        var nodes = (long)Performance.GetMonitor(Performance.Monitor.ObjectNodeCount);

        sb.Append("[render]\n");
        sb.Append($"  process : {procMs,6:F2} ms\n");
        sb.Append($"  physics : {physMs,6:F2} ms\n");
        sb.Append($"  draws   : {draws}\n");
        sb.Append($"  prims   : {prims:N0}\n");
        sb.Append($"  objects : {objs}   nodes: {nodes}\n");
        sb.Append($"  vram    : {vmem / (1024 * 1024):F1} MB\n");
    }

    private void AppendSim(StringBuilder sb)
    {
        var m = _runtime.Scheduler.Metrics;
        sb.Append($"[sim]   tick total: {m.LastTickMs:F2} ms\n");
        var rows = m.Systems
            .OrderByDescending(kv => kv.Value.AvgMs)
            .Take(12);
        foreach (var (name, s) in rows)
        {
            sb.Append($"  {name,-22} {s.LastMs,6:F2}  avg {s.AvgMs,6:F2}  max {s.MaxMs,6:F2}\n");
        }
    }

    private static Label MakeLabel(Vector2 pos, int fontSize)
    {
        var l = new Label
        {
            Position = pos,
            ZIndex = 1000,
        };
        l.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f));
        l.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f));
        l.AddThemeConstantOverride("outline_size", 4);
        l.AddThemeFontSizeOverride("font_size", fontSize);
        return l;
    }
}
