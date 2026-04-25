using Godot;

namespace CowColonySim.Game.UI;

public partial class FpsLabel : CanvasLayer
{
    private Label _label = null!;

    public override void _Ready()
    {
        _label = new Label
        {
            Position = new Vector2(8, 4),
            Text = "FPS: --",
        };
        _label.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f));
        _label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f));
        _label.AddThemeConstantOverride("outline_size", 4);
        AddChild(_label);
    }

    public override void _Process(double delta)
    {
        _label.Text = $"FPS: {Engine.GetFramesPerSecond()}";
    }
}
