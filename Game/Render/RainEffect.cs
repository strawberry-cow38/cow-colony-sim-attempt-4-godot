using Godot;

namespace CowColonySim.Game.Render;

// Rain particle field that follows the active camera. Spawns above the
// camera in a flat box and lets gravity carry the streaks down past the
// view. Unshaded billboard quads stretched along velocity = streaks
// instead of dots. Toggle with Toggle(); off by default.
public partial class RainEffect : Node3D
{
    private const float SpawnHeight = 40f;
    private const float SpawnHalfExtent = 60f;
    private const int Amount = 1200;

    private GpuParticles3D _particles = null!;
    private bool _enabled;

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
            Amount = Amount,
            Lifetime = 1.4f,
            Preprocess = 1.0f,
            VisibilityAabb = new Aabb(new Vector3(-SpawnHalfExtent, -SpawnHeight, -SpawnHalfExtent),
                                      new Vector3(SpawnHalfExtent * 2f, SpawnHeight * 2f, SpawnHalfExtent * 2f)),
            ProcessMaterial = process,
            DrawPass1 = mesh,
            Emitting = false,
            Position = new Vector3(0, SpawnHeight, 0),
        };
        AddChild(_particles);
    }

    public void Toggle()
    {
        _enabled = !_enabled;
        _particles.Emitting = _enabled;
    }

    public void SetEnabled(bool on)
    {
        _enabled = on;
        _particles.Emitting = on;
    }

    public override void _Process(double delta)
    {
        var cam = GetViewport().GetCamera3D();
        if (cam is null) return;
        var p = cam.GlobalPosition;
        GlobalPosition = new Vector3(p.X, p.Y, p.Z);
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
