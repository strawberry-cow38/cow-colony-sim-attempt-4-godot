using Godot;

namespace CowColonySim.Game.CameraRig;

// Yaw pivot (this Node3D) -> pitch child -> Camera3D offset by zoom distance.
// WASD pans on the ground plane relative to yaw, Q/E rotate yaw, wheel zooms,
// middle-mouse drag orbits (free yaw + clamped pitch). Pan speed scales with
// zoom so distant views move farther per keypress than close-ups.
public partial class GimbalCamera : Node3D
{
    [Export] public float PanSpeed { get; set; } = 1500f;
    [Export] public float BoostMultiplier { get; set; } = 4f;
    [Export] public float CrawlMultiplier { get; set; } = 0.25f;
    [Export] public float YawSpeed { get; set; } = 1.5f;
    [Export] public float OrbitSensitivity { get; set; } = 0.005f;
    [Export] public float MinPitch { get; set; } = -1.45f;
    [Export] public float MaxPitch { get; set; } = -0.1f;
    [Export] public float MinZoom { get; set; } = 200f;
    [Export] public float MaxZoom { get; set; } = 30000f;
    [Export] public float ZoomStep { get; set; } = 1.15f;

    private Node3D _pitchNode = null!;
    private Camera3D _camera = null!;
    private float _zoom = 4500f;
    private float _pitch = -1.0f;
    private bool _orbiting;

    public Camera3D Camera => _camera;

    public override void _Ready()
    {
        _pitchNode = new Node3D { Name = "Pitch" };
        AddChild(_pitchNode);
        _camera = new Camera3D
        {
            Name = "Camera",
            Fov = 60f,
            Near = 1f,
            Far = 80000f,
        };
        _pitchNode.AddChild(_camera);
        ApplyTransform();
    }

    public void SetFocus(Vector3 worldPos)
    {
        GlobalPosition = worldPos;
    }

    private void ApplyTransform()
    {
        _pitchNode.Rotation = new Vector3(_pitch, 0f, 0f);
        _camera.Position = new Vector3(0f, 0f, _zoom);
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Middle)
            {
                _orbiting = mb.Pressed;
                Input.MouseMode = _orbiting ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
            }
            else if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelUp)
            {
                _zoom = Mathf.Clamp(_zoom / ZoomStep, MinZoom, MaxZoom);
                ApplyTransform();
            }
            else if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelDown)
            {
                _zoom = Mathf.Clamp(_zoom * ZoomStep, MinZoom, MaxZoom);
                ApplyTransform();
            }
        }
        else if (@event is InputEventMouseMotion mm && _orbiting)
        {
            var yaw = Rotation.Y - mm.Relative.X * OrbitSensitivity;
            _pitch = Mathf.Clamp(_pitch - mm.Relative.Y * OrbitSensitivity, MinPitch, MaxPitch);
            Rotation = new Vector3(0f, yaw, 0f);
            ApplyTransform();
        }
    }

    public override void _Process(double delta)
    {
        var dt = (float)delta;

        var yawRot = Rotation.Y;
        var yawDelta = 0f;
        if (Input.IsKeyPressed(Key.Q)) yawDelta += YawSpeed * dt;
        if (Input.IsKeyPressed(Key.E)) yawDelta -= YawSpeed * dt;
        if (yawDelta != 0f)
        {
            yawRot += yawDelta;
            Rotation = new Vector3(0f, yawRot, 0f);
        }

        var dir = Vector2.Zero;
        if (Input.IsKeyPressed(Key.W)) dir.Y += 1f;
        if (Input.IsKeyPressed(Key.S)) dir.Y -= 1f;
        if (Input.IsKeyPressed(Key.A)) dir.X -= 1f;
        if (Input.IsKeyPressed(Key.D)) dir.X += 1f;

        if (dir == Vector2.Zero) return;
        dir = dir.Normalized();

        var speed = PanSpeed;
        if (Input.IsKeyPressed(Key.Shift)) speed *= BoostMultiplier;
        if (Input.IsKeyPressed(Key.Ctrl)) speed *= CrawlMultiplier;

        var fwd = new Vector3(-Mathf.Sin(yawRot), 0f, -Mathf.Cos(yawRot));
        var right = new Vector3(Mathf.Cos(yawRot), 0f, -Mathf.Sin(yawRot));
        var zoomScale = _zoom / 4500f + 0.25f;
        GlobalPosition += (right * dir.X + fwd * dir.Y) * speed * dt * zoomScale;
    }
}
