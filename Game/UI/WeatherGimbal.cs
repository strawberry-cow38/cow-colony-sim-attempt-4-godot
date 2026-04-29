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

    private SubViewport _viewport = null!;
    private Node3D _compass = null!;
    private Node3D _arrow = null!;
    private CameraRig? _rig;
    private SnapshotPublisher? _publisher;

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

        // Top-down ortho camera. With X-rotation -90deg the camera looks
        // straight down -Y; camera-up maps to world +Z, camera-right maps
        // to world +X. So world (+X = east, +Z = north) is screen
        // (right = east, up = north) before any compass rotation.
        var cam = new Camera3D
        {
            Projection = Camera3D.ProjectionType.Orthogonal,
            Size = 1.6f,
            Current = true,
            Position = new Vector3(0f, 4f, 0f),
            RotationDegrees = new Vector3(-90f, 0f, 0f),
            Far = 50f,
            Near = 0.1f,
        };
        _viewport.AddChild(cam);

        _compass = new Node3D { Name = "Compass" };
        _viewport.AddChild(_compass);

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
        // Compass yaw cancels camera yaw: world +Z always renders toward
        // screen-up direction implied by the camera framing.
        _compass.Rotation = new Vector3(0f, -_rig.Yaw, 0f);
        // Arrow points TOWARD the wind heading in world space.
        _arrow.Rotation = new Vector3(0f, snap.Weather.CurrentWindRad, 0f);
    }
}
