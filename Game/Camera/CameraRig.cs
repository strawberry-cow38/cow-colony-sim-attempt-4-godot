using Godot;

namespace CowColonySim.Game.Camera;

// Yaw pivot at ground level + pitch pivot child + Camera3D back along local Z.
// WASD pans XZ in view-rotated world space, Q/E snap-rotate yaw 90 deg with
// a smooth tween, middle-mouse drag pans. Pivot stays clamped inside the
// configured cell bounds so the camera can't wander off the main cell.
public partial class CameraRig : Node3D
{
    private const float MoveSpeed = 80f;
    private const float MiddleDragSpeed = 1.2f;
    private const float SnapRotateSeconds = 0.18f;
    private const float SnapStepRad = Mathf.Pi / 2f;
    private const float PitchDeg = -50f;
    private const float Distance = 120f;

    private Camera3D _camera = null!;
    private Node3D _pitchPivot = null!;
    private float _yaw;
    private float _yawTarget;
    private float _yawTweenFrom;
    private float _yawTweenT;
    private bool _yawTweening;
    private bool _middleHeld;
    private Vector2 _boundsMax = new(2048f, 2048f);

    public void Configure(Vector2 boundsMax, Vector2 startCenter)
    {
        _boundsMax = boundsMax;
        Position = new Vector3(startCenter.X, 0f, startCenter.Y);
    }

    public override void _Ready()
    {
        _pitchPivot = new Node3D { Name = "Pitch" };
        _pitchPivot.RotationDegrees = new Vector3(PitchDeg, 0f, 0f);
        AddChild(_pitchPivot);

        _camera = new Camera3D
        {
            Name = "Camera",
            Position = new Vector3(0f, 0f, Distance),
            Far = 20_000f,
        };
        _pitchPivot.AddChild(_camera);
    }

    public override void _Process(double delta)
    {
        var dt = (float)delta;

        var inputX = (Input.IsKeyPressed(Key.D) ? 1f : 0f) - (Input.IsKeyPressed(Key.A) ? 1f : 0f);
        var inputZ = (Input.IsKeyPressed(Key.S) ? 1f : 0f) - (Input.IsKeyPressed(Key.W) ? 1f : 0f);
        if (inputX != 0f || inputZ != 0f)
        {
            var yawBasis = Basis.FromEuler(new Vector3(0f, _yaw, 0f));
            var move = yawBasis * new Vector3(inputX, 0f, inputZ);
            Position += move * MoveSpeed * dt;
            ClampToBounds();
        }

        if (_yawTweening)
        {
            _yawTweenT += dt / SnapRotateSeconds;
            if (_yawTweenT >= 1f)
            {
                _yawTweenT = 1f;
                _yawTweening = false;
                _yaw = _yawTarget;
            }
            else
            {
                _yaw = Mathf.Lerp(_yawTweenFrom, _yawTarget, SmoothStep(_yawTweenT));
            }
            Rotation = new Vector3(0f, _yaw, 0f);
        }
    }

    public override void _Input(InputEvent ev)
    {
        switch (ev)
        {
            case InputEventKey k when k.Pressed && !k.Echo:
                if (k.Keycode == Key.Q) StartYawTween(+SnapStepRad);
                else if (k.Keycode == Key.E) StartYawTween(-SnapStepRad);
                break;

            case InputEventMouseButton mb when mb.ButtonIndex == MouseButton.Middle:
                _middleHeld = mb.Pressed;
                break;

            case InputEventMouseMotion mm when _middleHeld:
                var yawBasis = Basis.FromEuler(new Vector3(0f, _yaw, 0f));
                var pan = yawBasis * new Vector3(-mm.Relative.X, 0f, -mm.Relative.Y) * MiddleDragSpeed;
                Position += pan;
                ClampToBounds();
                break;
        }
    }

    private void StartYawTween(float deltaRad)
    {
        if (_yawTweening)
        {
            _yawTarget += deltaRad;
        }
        else
        {
            _yawTarget = _yaw + deltaRad;
            _yawTweening = true;
        }
        _yawTweenFrom = _yaw;
        _yawTweenT = 0f;
    }

    private void ClampToBounds()
    {
        var p = Position;
        p.X = Mathf.Clamp(p.X, 0f, _boundsMax.X);
        p.Z = Mathf.Clamp(p.Z, 0f, _boundsMax.Y);
        Position = p;
    }

    private static float SmoothStep(float t) => t * t * (3f - 2f * t);
}
