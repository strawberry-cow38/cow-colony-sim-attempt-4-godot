using Godot;

namespace CowColonySim.Game.CameraRig;

// Right-mouse drag to look. WASD moves on the ground plane relative to yaw,
// Q/E lower/raise. Shift = boost, Ctrl = crawl. Scroll wheel adjusts base
// speed. Middle-mouse drag pans on the camera plane.
public partial class FlyCamera : Camera3D
{
    [Export] public float BaseSpeed { get; set; } = 1500f;
    [Export] public float BoostMultiplier { get; set; } = 4f;
    [Export] public float CrawlMultiplier { get; set; } = 0.25f;
    [Export] public float MouseSensitivity { get; set; } = 0.0035f;
    [Export] public float PanSpeed { get; set; } = 4f;
    [Export] public float MinPitch { get; set; } = -1.5f;
    [Export] public float MaxPitch { get; set; } = 1.5f;
    [Export] public float MinSpeed { get; set; } = 100f;
    [Export] public float MaxSpeed { get; set; } = 20000f;

    private bool _looking;
    private bool _panning;

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Right)
            {
                _looking = mb.Pressed;
                Input.MouseMode = _looking ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
            }
            else if (mb.ButtonIndex == MouseButton.Middle)
            {
                _panning = mb.Pressed;
            }
            else if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelUp)
            {
                BaseSpeed = Mathf.Clamp(BaseSpeed * 1.15f, MinSpeed, MaxSpeed);
            }
            else if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelDown)
            {
                BaseSpeed = Mathf.Clamp(BaseSpeed / 1.15f, MinSpeed, MaxSpeed);
            }
        }
        else if (@event is InputEventMouseMotion mm)
        {
            if (_looking)
            {
                var euler = Rotation;
                var yaw = euler.Y - mm.Relative.X * MouseSensitivity;
                var pitch = Mathf.Clamp(euler.X - mm.Relative.Y * MouseSensitivity, MinPitch, MaxPitch);
                Rotation = new Vector3(pitch, yaw, 0f);
            }
            else if (_panning)
            {
                var right = GlobalTransform.Basis.X;
                var up = GlobalTransform.Basis.Y;
                var delta = -right * (mm.Relative.X * PanSpeed) + up * (mm.Relative.Y * PanSpeed);
                GlobalPosition += delta;
            }
        }
    }

    public override void _Process(double delta)
    {
        var dir = Vector3.Zero;
        if (Input.IsKeyPressed(Key.W)) dir.Z -= 1f;
        if (Input.IsKeyPressed(Key.S)) dir.Z += 1f;
        if (Input.IsKeyPressed(Key.A)) dir.X -= 1f;
        if (Input.IsKeyPressed(Key.D)) dir.X += 1f;
        if (Input.IsKeyPressed(Key.E)) dir.Y += 1f;
        if (Input.IsKeyPressed(Key.Q)) dir.Y -= 1f;

        if (dir == Vector3.Zero) return;
        dir = dir.Normalized();

        var speed = BaseSpeed;
        if (Input.IsKeyPressed(Key.Shift)) speed *= BoostMultiplier;
        if (Input.IsKeyPressed(Key.Ctrl)) speed *= CrawlMultiplier;

        var basis = GlobalTransform.Basis;
        var move = basis.X * dir.X + Vector3.Up * dir.Y + basis.Z * dir.Z;
        GlobalPosition += move * speed * (float)delta;
    }
}
