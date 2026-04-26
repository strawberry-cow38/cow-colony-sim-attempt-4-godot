using CowColonySim.Sim.Logging;
using Godot;

namespace CowColonySim.Game.Camera;

// Yaw pivot at ground level + pitch pivot child + Camera3D back along local Z.
// WASD pans XZ in view-rotated world space, Q/E snap-rotate yaw 90 deg with
// a smooth tween, middle-mouse drag pans. Pivot stays clamped inside the
// configured cell bounds so the camera can't wander off the main cell.
public partial class CameraRig : Node3D
{
    private const float MoveSpeed = 80f;
    private const float SnapRotateSeconds = 0.18f;
    private const float SnapStepRad = Mathf.Pi / 2f;
    private const float PitchDeg = -50f;
    private const float Distance = 120f;
    private const float MinDistance = 30f;
    private const float MaxDistance = 900f;
    private const float ZoomStep = 1.18f;
    private const float ZoomLerp = 12f;
    private const float OrbitYawDegPerPx = 0.3f;
    private const float OrbitPitchDegPerPx = 0.2f;
    private const float MinPitchDeg = -85f;
    private const float MaxPitchDeg = -10f;

    private Camera3D _camera = null!;
    private Node3D _pitchPivot = null!;
    private float _yaw;
    private float _yawTarget;
    private float _yawTweenFrom;
    private float _yawTweenT;
    private bool _yawTweening;
    private bool _middleHeld;
    private float _pitchDeg = PitchDeg;
    private float _distance = Distance;
    private float _distanceTarget = Distance;
    private Vector2 _boundsMax = new(2048f, 2048f);
    private bool _loggedFirstMove;
    private bool _loggedFirstKey;

    public void Configure(Vector2 boundsMax, Vector2 startCenter)
    {
        _boundsMax = boundsMax;
        Position = new Vector3(startCenter.X, 0f, startCenter.Y);
    }

    public override void _Ready()
    {
        _pitchPivot = new Node3D { Name = "Pitch" };
        _pitchPivot.RotationDegrees = new Vector3(_pitchDeg, 0f, 0f);
        AddChild(_pitchPivot);

        _camera = new Camera3D
        {
            Name = "Camera",
            Position = new Vector3(0f, 0f, _distance),
            Far = 20_000f,
            Current = true,
        };
        _pitchPivot.AddChild(_camera);
        _camera.MakeCurrent();

        SimLog.Logger.Information(
            "CameraRig ready. Pivot at {Pos}, bounds {Bounds}, distance {Dist}.",
            Position, _boundsMax, _distance);
    }

    public override void _Process(double delta)
    {
        var dt = (float)delta;

        var inputX = (Input.IsPhysicalKeyPressed(Key.D) ? 1f : 0f) - (Input.IsPhysicalKeyPressed(Key.A) ? 1f : 0f);
        var inputZ = (Input.IsPhysicalKeyPressed(Key.S) ? 1f : 0f) - (Input.IsPhysicalKeyPressed(Key.W) ? 1f : 0f);
        if (inputX != 0f || inputZ != 0f)
        {
            var yawBasis = Basis.FromEuler(new Vector3(0f, _yaw, 0f));
            var move = yawBasis * new Vector3(inputX, 0f, inputZ);
            var distRatio = Mathf.Clamp(_distance / Distance, 0.25f, 5f);
            var shift = Input.IsPhysicalKeyPressed(Key.Shift) || Input.IsKeyPressed(Key.Shift);
            var speed = MoveSpeed * distRatio * (shift ? 2f : 1f);
            Position += move * speed * dt;
            ClampToBounds();

            if (!_loggedFirstMove)
            {
                _loggedFirstMove = true;
                SimLog.Logger.Information(
                    "CameraRig WASD detected (X={X}, Z={Z}). Pivot now {Pos}.",
                    inputX, inputZ, Position);
            }
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

        if (!Mathf.IsEqualApprox(_distance, _distanceTarget))
        {
            _distance = Mathf.Lerp(_distance, _distanceTarget, 1f - Mathf.Exp(-ZoomLerp * dt));
            _camera.Position = new Vector3(0f, 0f, _distance);
        }
    }

    public override void _UnhandledInput(InputEvent ev)
    {
        if (ev is InputEventKey ke && ke.Pressed && !ke.Echo && !_loggedFirstKey)
        {
            _loggedFirstKey = true;
            SimLog.Logger.Information(
                "CameraRig first key event: keycode={KC}, physical={PK}.",
                ke.Keycode, ke.PhysicalKeycode);
        }

        switch (ev)
        {
            case InputEventKey k when k.Pressed && !k.Echo:
                if (k.PhysicalKeycode == Key.Q) StartYawTween(+SnapStepRad);
                else if (k.PhysicalKeycode == Key.E) StartYawTween(-SnapStepRad);
                break;

            case InputEventMouseButton mb when mb.ButtonIndex == MouseButton.Middle:
                _middleHeld = mb.Pressed;
                break;

            case InputEventMouseButton wheel when wheel.Pressed && wheel.ButtonIndex == MouseButton.WheelUp:
                _distanceTarget = Mathf.Clamp(_distanceTarget / ZoomStep, MinDistance, MaxDistance);
                break;

            case InputEventMouseButton wheel when wheel.Pressed && wheel.ButtonIndex == MouseButton.WheelDown:
                _distanceTarget = Mathf.Clamp(_distanceTarget * ZoomStep, MinDistance, MaxDistance);
                break;

            case InputEventMouseMotion mm when _middleHeld:
                if (_yawTweening)
                {
                    _yawTweening = false;
                    _yawTarget = _yaw;
                }
                _yaw -= Mathf.DegToRad(mm.Relative.X * OrbitYawDegPerPx);
                Rotation = new Vector3(0f, _yaw, 0f);
                _pitchDeg = Mathf.Clamp(
                    _pitchDeg - mm.Relative.Y * OrbitPitchDegPerPx,
                    MinPitchDeg, MaxPitchDeg);
                _pitchPivot.RotationDegrees = new Vector3(_pitchDeg, 0f, 0f);
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
