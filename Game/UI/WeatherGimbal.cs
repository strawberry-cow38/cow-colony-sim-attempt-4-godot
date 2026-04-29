using CowColonySim.Game.Camera;
using CowColonySim.Sim.Snapshots;
using Godot;

namespace CowColonySim.Game.UI;

// Bottom-right wind compass. A SubViewport renders an isolated 3D scene:
// a fixed top-down ortho camera looks at a "compass" Node3D whose yaw
// tracks -CameraRig.Yaw so world-north (cardinal "N") always stays
// aligned with the world. Inside the compass an arrow Node3D points at
// WeatherSystem.CurrentWindRad, so the visible arrow shows wind
// direction relative to the player's current camera framing.
public partial class WeatherGimbal : CanvasLayer
{
    private const int Size = 168;
    private const int Margin = 18;

    // Spring constants tuned so a 90deg Q/E flick overshoots a few
    // degrees then settles in ~0.6s. Compass body is heavier (slower,
    // less wobble) than the needle so the cardinals don't slosh around
    // distractingly when the camera snaps.
    private const float CompassStiffness = 70f;
    private const float CompassDamping = 11f;
    private const float ArrowStiffness = 95f;
    private const float ArrowDamping = 7f;

    private SubViewport _viewport = null!;
    private Node3D _compass = null!;
    private Node3D _arrow = null!;
    private CameraRig? _rig;
    private SnapshotPublisher? _publisher;
    private float _compassYaw;
    private float _compassVel;
    private float _arrowYaw;
    private float _arrowVel;
    private bool _initialized;

    public void Configure(CameraRig rig, SnapshotPublisher publisher)
    {
        _rig = rig;
        _publisher = publisher;
    }

    public override void _Ready()
    {
        Layer = 50;

        var root = new Control
        {
            Name = "GimbalRoot",
            AnchorLeft = 1f, AnchorRight = 1f,
            AnchorTop = 1f, AnchorBottom = 1f,
            OffsetLeft = -(Size + Margin),
            OffsetTop = -(Size + Margin),
            OffsetRight = -Margin,
            OffsetBottom = -Margin,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        AddChild(root);

        var container = new SubViewportContainer
        {
            Stretch = true,
            AnchorLeft = 0f, AnchorRight = 1f,
            AnchorTop = 0f, AnchorBottom = 1f,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        root.AddChild(container);

        _viewport = new SubViewport
        {
            Size = new Vector2I(Size, Size),
            TransparentBg = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            OwnWorld3D = true,
            World3D = new World3D(),
        };
        container.AddChild(_viewport);

        var env = new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.ClearColor,
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(1f, 1f, 1f),
                AmbientLightEnergy = 1.4f,
            },
        };
        _viewport.AddChild(env);

        // 45-degree isometric ortho cam. Camera sits up + back along
        // (+Y, +Z), tilted -45deg X so it looks down-forward at the
        // compass. World +Z still projects toward screen-up (slightly
        // foreshortened), world +X = screen-right, so the compass yaw
        // logic below carries through unchanged.
        var cam = new Camera3D
        {
            Projection = Camera3D.ProjectionType.Orthogonal,
            Size = 1.9f,
            Current = true,
            Position = new Vector3(0f, 3f, 3f),
            RotationDegrees = new Vector3(-45f, 0f, 0f),
            Far = 50f,
            Near = 0.1f,
        };
        _viewport.AddChild(cam);

        _compass = new Node3D { Name = "Compass" };
        _viewport.AddChild(_compass);

        // Fixed "you face this way" chevron pinned to the top of the
        // gimbal (world +Z in compass scene). Lives outside _compass so
        // it never rotates — whichever cardinal lines up under it is the
        // camera's forward heading.
        var heading = new MeshInstance3D
        {
            Name = "HeadingMarker",
            Mesh = new BoxMesh { Size = new Vector3(0.18f, 0.05f, 0.18f) },
            MaterialOverride = MakeMat(new Color(0.95f, 0.95f, 1f)),
            RotationDegrees = new Vector3(0f, 45f, 0f),
            Position = new Vector3(0f, 0.18f, 1.0f),
        };
        _viewport.AddChild(heading);

        // Decorative ring sitting just below the arrow so it never z-fights.
        var ring = new MeshInstance3D
        {
            Mesh = new TorusMesh
            {
                InnerRadius = 0.62f,
                OuterRadius = 0.72f,
                RingSegments = 48,
                Rings = 8,
            },
            MaterialOverride = MakeMat(new Color(0.18f, 0.18f, 0.22f, 0.75f), transparent: true),
            Position = new Vector3(0f, -0.1f, 0f),
        };
        _compass.AddChild(ring);

        AddCardinalLabel("N", new Vector3(0f, 0.05f, 0.82f), new Color(1f, 0.55f, 0.55f));
        AddCardinalLabel("S", new Vector3(0f, 0.05f, -0.82f), new Color(0.85f, 0.85f, 0.9f));
        AddCardinalLabel("E", new Vector3(0.82f, 0.05f, 0f), new Color(0.85f, 0.85f, 0.9f));
        AddCardinalLabel("W", new Vector3(-0.82f, 0.05f, 0f), new Color(0.85f, 0.85f, 0.9f));

        // Arrow points at +Z by default. Rotating compass by world wind
        // angle aims it at world wind direction; compass' parent rotation
        // (-cameraYaw) then re-frames it relative to the camera.
        _arrow = new Node3D { Name = "Arrow" };
        var shaftMat = MakeMat(new Color(1f, 0.85f, 0.25f));
        var headMat = MakeMat(new Color(1f, 0.45f, 0.1f));

        var shaft = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.10f, 0.08f, 0.55f) },
            MaterialOverride = shaftMat,
            Position = new Vector3(0f, 0f, -0.05f),
        };
        var head = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.24f, 0.08f, 0.24f) },
            MaterialOverride = headMat,
            RotationDegrees = new Vector3(0f, 45f, 0f),
            Position = new Vector3(0f, 0f, 0.30f),
        };
        var tail = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.18f, 0.06f, 0.06f) },
            MaterialOverride = shaftMat,
            Position = new Vector3(0f, 0f, -0.35f),
        };
        _arrow.AddChild(shaft);
        _arrow.AddChild(head);
        _arrow.AddChild(tail);
        _compass.AddChild(_arrow);
    }

    private void AddCardinalLabel(string text, Vector3 pos, Color color)
    {
        var lbl = new Label3D
        {
            Text = text,
            Position = pos,
            FontSize = 96,
            OutlineSize = 12,
            Modulate = color,
            OutlineModulate = new Color(0f, 0f, 0f, 0.85f),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            PixelSize = 0.0035f,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _compass.AddChild(lbl);
    }

    private static StandardMaterial3D MakeMat(Color c, bool transparent = false)
    {
        return new StandardMaterial3D
        {
            AlbedoColor = c,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = transparent
                ? BaseMaterial3D.TransparencyEnum.Alpha
                : BaseMaterial3D.TransparencyEnum.Disabled,
        };
    }

    public override void _Process(double delta)
    {
        if (_rig is null || _publisher is null) return;
        var snap = _publisher.Current;
        var dt = (float)delta;

        var compassTarget = -_rig.Yaw;
        var arrowTarget = snap.Weather.CurrentWindRad;

        if (!_initialized)
        {
            _compassYaw = compassTarget;
            _arrowYaw = arrowTarget;
            _initialized = true;
        }

        SpringStep(ref _compassYaw, ref _compassVel, compassTarget, CompassStiffness, CompassDamping, dt);
        SpringStep(ref _arrowYaw, ref _arrowVel, arrowTarget, ArrowStiffness, ArrowDamping, dt);

        _compass.Rotation = new Vector3(0f, _compassYaw, 0f);
        _arrow.Rotation = new Vector3(0f, _arrowYaw, 0f);
    }

    // Critically-undamped angular spring: chases target along the shortest
    // arc so a 90deg flick overshoots a touch then settles instead of
    // unwrapping the long way around. Sub-stepping at >=120 Hz keeps the
    // integrator stable when the frame dt drifts.
    private static void SpringStep(ref float angle, ref float vel, float target, float k, float d, float dt)
    {
        var steps = Mathf.Max(1, (int)MathF.Ceiling(dt * 120f));
        var sub = dt / steps;
        for (var i = 0; i < steps; i++)
        {
            var diff = Mathf.Wrap(target - angle, -Mathf.Pi, Mathf.Pi);
            var accel = k * diff - d * vel;
            vel += accel * sub;
            angle += vel * sub;
        }
    }
}
