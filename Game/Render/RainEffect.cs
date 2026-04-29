using CowColonySim.Sim.Snapshots;
using Godot;

namespace CowColonySim.Game.Render;

// Rain particle field that follows the active camera. Spawns above the
// camera in a flat box and lets gravity carry the streaks down past the
// view. Unshaded billboard quads stretched along velocity = streaks
// instead of dots. Density tracks WeatherView.CurrentRainfall when a
// SnapshotPublisher is wired in; otherwise stays at MaxAmount.
public partial class RainEffect : Node3D
{
    private const float SpawnHeight = 40f;
    private const float SpawnHalfExtent = 60f;
    private const int MaxAmount = 1800;
    // RainCurve outputs a 0..1 intensity that rarely saturates in normal
    // climate cycles — boost so even moderate rainfall looks like rain.
    private const float IntensityGain = 1.5f;

    private GpuParticles3D _particles = null!;
    private SnapshotPublisher? _publisher;
    private bool _manualOverride;
    private bool _enabled = true;

    public override void _Ready()
    {
        var process = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(SpawnHalfExtent, 0.1f, SpawnHalfExtent),
            Direction = new Vector3(0, -1, 0),
            Spread = 1f,
            InitialVelocityMin = 60f,
            InitialVelocityMax = 80f,
            Gravity = new Vector3(0, -120f, 0),
            ScaleMin = 0.6f,
            ScaleMax = 1.2f,
        };

        var mat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
            BillboardKeepScale = true,
            AlbedoColor = new Color(0.7f, 0.85f, 1f, 0.55f),
            ParticlesAnimHFrames = 1,
            ParticlesAnimVFrames = 1,
            VertexColorUseAsAlbedo = true,
        };

        var mesh = new QuadMesh
        {
            Size = new Vector2(0.05f, 1.6f),
            Material = mat,
        };

        _particles = new GpuParticles3D
        {
            Name = "RainParticles",
            Amount = MaxAmount,
            Lifetime = 1.4f,
            Preprocess = 1.0f,
            VisibilityAabb = new Aabb(new Vector3(-SpawnHalfExtent, -SpawnHeight, -SpawnHalfExtent),
                                      new Vector3(SpawnHalfExtent * 2f, SpawnHeight * 2f, SpawnHalfExtent * 2f)),
            ProcessMaterial = process,
            DrawPass1 = mesh,
            Emitting = false,
            AmountRatio = 0f,
            Position = new Vector3(0, SpawnHeight, 0),
        };
        AddChild(_particles);
    }

    public void Configure(SnapshotPublisher publisher)
    {
        _publisher = publisher;
    }

    // Force on/off independent of weather sim — Ctrl+R toggles this and
    // pins density to either max or zero until cleared.
    public void Toggle()
    {
        _manualOverride = !_manualOverride;
        _enabled = _manualOverride;
        ApplyIntensity(_manualOverride ? 1f : 0f);
    }

    public void SetEnabled(bool on)
    {
        _manualOverride = on;
        _enabled = on;
        ApplyIntensity(on ? 1f : 0f);
    }

    public override void _Process(double delta)
    {
        var cam = GetViewport().GetCamera3D();
        if (cam is not null)
        {
            var p = cam.GlobalPosition;
            GlobalPosition = new Vector3(p.X, p.Y, p.Z);
        }

        if (_manualOverride) return;
        if (_publisher is null) return;
        var snap = _publisher.Current;
        var t = Mathf.Clamp(snap.Weather.CurrentRainfall * IntensityGain, 0f, 1f);
        ApplyIntensity(t);
    }

    private void ApplyIntensity(float t)
    {
        _particles.AmountRatio = t;
        _particles.Emitting = t > 0.001f;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey k || !k.Pressed || k.Echo) return;
        if (k.Keycode == Key.R && k.CtrlPressed)
        {
            Toggle();
            GetViewport().SetInputAsHandled();
        }
    }
}
