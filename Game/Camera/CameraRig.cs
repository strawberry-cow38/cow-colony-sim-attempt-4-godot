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
    private const float DefaultMaxDistance = 900f;
    private const float ZoomStep = 1.18f;
    private const float ZoomLerp = 12f;
    // Exponential smoothing rate for portrait-driven focus moves. Higher = snappier.
    private const float FocusLerp = 9f;
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
    private float _maxDistance = DefaultMaxDistance;
    // When non-null, _Process lerps Position toward this point and clears
    // it once the rig is essentially on top. Keyboard pan or middle-drag
    // also clear the target so player input wins over the focus glide.
    private Vector3? _focusTarget;
    // Hard-follow mode driven by the portrait double-click. The owner
    // (PortraitBar) calls FollowAt every frame to keep the rig glued to
    // a moving entity; any keyboard pan breaks the lock.
    private bool _isFollowing;
    public bool IsFollowing => _isFollowing;

    public float Yaw => _yaw;
    public float CurrentDistance => _distance;
    public float MinZoomDistance => MinDistance;
    public float MaxZoomDistance => _maxDistance;
    // 0% = fully zoomed out (max distance), 100% = max zoom in (min distance).
    public float ZoomPercent =>
        _maxDistance > MinDistance
            ? Mathf.Clamp(100f * (_maxDistance - _distance) / (_maxDistance - MinDistance), 0f, 100f)
            : 0f;
    private bool _loggedFirstMove;
    private bool _loggedFirstKey;

    public void Configure(Vector2 boundsMax, Vector2 startCenter, float? maxDistance = null)
    {
        _boundsMax = boundsMax;
        _maxDistance = maxDistance ?? DefaultMaxDistance;
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
            // Player took manual control — abort any in-progress focus glide
            // or hard-follow lock so the rig doesn't fight the keyboard.
            _focusTarget = null;
            _isFollowing = false;
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

        if (_focusTarget is Vector3 target)
        {
            var t = 1f - Mathf.Exp(-FocusLerp * dt);
            var p = Position;
            p.X = Mathf.Lerp(p.X, target.X, t);
            p.Z = Mathf.Lerp(p.Z, target.Z, t);
            Position = p;
            ClampToBounds();
            // Stop the lerp once we're within ~0.05 tile so we don't burn
            // cycles inching toward the target forever.
            var stopThreshold = 2f;
            if (Mathf.Abs(p.X - target.X) < stopThreshold && Mathf.Abs(p.Z - target.Z) < stopThreshold)
            {
                Position = new Vector3(target.X, p.Y, target.Z);
                ClampToBounds();
                _focusTarget = null;
            }
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
                if (k.PhysicalKeycode == Key.Q) SnapToNextCardinal(+1);
                else if (k.PhysicalKeycode == Key.E) SnapToNextCardinal(-1);
                break;

            case InputEventMouseButton mb when mb.ButtonIndex == MouseButton.Middle:
                _middleHeld = mb.Pressed;
                break;

            case InputEventMouseButton wheel when wheel.Pressed && wheel.ButtonIndex == MouseButton.WheelUp:
                _distanceTarget = Mathf.Clamp(_distanceTarget / ZoomStep, MinDistance, _maxDistance);
                break;

            case InputEventMouseButton wheel when wheel.Pressed && wheel.ButtonIndex == MouseButton.WheelDown:
                _distanceTarget = Mathf.Clamp(_distanceTarget * ZoomStep, MinDistance, _maxDistance);
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

    // Snap to the next cardinal yaw in the given direction (+1 = CCW/Q, -1 = CW/E).
    // Free-orbit middle-drag leaves _yaw at arbitrary angles, so Q/E should
    // always land back on a cardinal instead of stacking 90deg deltas.
    private void SnapToNextCardinal(int dir)
    {
        var step = SnapStepRad;
        var basis = _yawTweening ? _yawTarget : _yaw;
        float target;
        if (dir > 0)
        {
            var k = Mathf.FloorToInt(basis / step + 0.001f);
            target = (k + 1) * step;
        }
        else
        {
            var k = Mathf.CeilToInt(basis / step - 0.001f);
            target = (k - 1) * step;
        }
        _yawTweenFrom = _yaw;
        _yawTarget = target;
        _yawTweening = true;
        _yawTweenT = 0f;
    }

    // Smoothly slide the rig pivot toward a world-space (X, Z) point.
    // _Process picks up the target and lerps each frame; player input
    // (WASD / middle-drag pan) cancels the glide. Used by the portrait
    // bar to focus on a colonist without snapping the camera.
    public void FocusOnUnits(float unitsX, float unitsZ)
    {
        _focusTarget = new Vector3(unitsX, 0f, unitsZ);
    }

    // Begin a hard-follow lock. The owner must keep calling FollowAt each
    // frame to drive the rig; WASD breaks the lock by clearing _isFollowing.
    public void BeginFollow()
    {
        _isFollowing = true;
        _focusTarget = null;
    }

    // Drive the rig toward a follow target. Lerps in (so the initial
    // double-click handoff stays smooth) and snaps once close enough so
    // tracking a moving colonist doesn't trail forever. No-op once the
    // player breaks the lock — a stale per-frame call won't yank back.
    public void FollowAt(float unitsX, float unitsZ)
    {
        if (!_isFollowing) return;
        var dt = (float)GetProcessDeltaTime();
        var t = 1f - Mathf.Exp(-FocusLerp * dt);
        var p = Position;
        var dx = unitsX - p.X;
        var dz = unitsZ - p.Z;
        if (Mathf.Abs(dx) < 1f && Mathf.Abs(dz) < 1f)
        {
            p.X = unitsX;
            p.Z = unitsZ;
        }
        else
        {
            p.X = Mathf.Lerp(p.X, unitsX, t);
            p.Z = Mathf.Lerp(p.Z, unitsZ, t);
        }
        Position = p;
        ClampToBounds();
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
