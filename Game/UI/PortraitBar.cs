using CowColonySim.Game.Camera;
using CowColonySim.Game.Selection;
using CowColonySim.Sim;
using CowColonySim.Sim.Snapshots;
using Godot;

namespace CowColonySim.Game.UI;

// Top-center bar of one portrait per colonist. Each portrait is a tiny
// SubViewport rendering a 3D body (capsule today, clothed mesh later)
// over a tinted background, framed in a Button so clicks focus the
// camera + select the colonist. Portraits are rebuilt only when the
// colonist roster changes — per-frame work just moves the camera +
// updates the highlight.
public partial class PortraitBar : CanvasLayer
{
    private const int PortraitWidth = 88;
    private const int PortraitHeight = 112;
    private const float CapsuleRadiusMeters = 0.25f;
    private const float CapsuleHeightMeters = 1.7f;
    private const float UnitsPerMeter = SimConstants.GodotUnitsPerTile / SimConstants.MetersPerTile;

    private SelectionService _selection = null!;
    private SnapshotPublisher _publisher = null!;
    private CameraRig _cameraRig = null!;

    private HBoxContainer _bar = null!;
    private readonly List<PortraitSlot> _slots = new();

    private sealed class PortraitSlot
    {
        public int EntityId;
        public Button Button = null!;
        public Panel HighlightFrame = null!;
    }

    public void Configure(SelectionService selection, SnapshotPublisher publisher, CameraRig cameraRig)
    {
        _selection = selection;
        _publisher = publisher;
        _cameraRig = cameraRig;
    }

    public override void _Ready()
    {
        Layer = 5;
        var root = new Control
        {
            AnchorLeft = 0f, AnchorRight = 1f, AnchorTop = 0f, AnchorBottom = 0f,
            OffsetLeft = 0f, OffsetRight = 0f, OffsetTop = 8f, OffsetBottom = PortraitHeight + 16f,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        AddChild(root);

        _bar = new HBoxContainer
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0f, AnchorBottom = 0f,
            GrowHorizontal = Control.GrowDirection.Both,
        };
        _bar.AddThemeConstantOverride("separation", 8);
        root.AddChild(_bar);
    }

    public override void _Process(double delta)
    {
        var snap = _publisher.Current;
        SyncRoster(snap.Colonists);
        UpdateHighlights();
    }

    private void SyncRoster(IReadOnlyList<ColonistView> colonists)
    {
        var same = _slots.Count == colonists.Count;
        if (same)
        {
            for (var i = 0; i < colonists.Count; i++)
            {
                if (_slots[i].EntityId != colonists[i].EntityId) { same = false; break; }
            }
        }
        if (same) return;

        foreach (var slot in _slots) slot.Button.QueueFree();
        _slots.Clear();
        foreach (var child in _bar.GetChildren()) child.QueueFree();

        for (var i = 0; i < colonists.Count; i++)
        {
            var c = colonists[i];
            var slot = BuildSlot(c.EntityId);
            _slots.Add(slot);
            _bar.AddChild(slot.Button);
        }

        // Center the bar after children settle (HBoxContainer min-size only
        // updates next frame, so re-centering then keeps things tidy).
        CallDeferred(nameof(RecenterBar));
    }

    private void RecenterBar()
    {
        var w = _bar.Size.X;
        _bar.OffsetLeft = -w * 0.5f;
        _bar.OffsetRight = w * 0.5f;
    }

    private PortraitSlot BuildSlot(int entityId)
    {
        var btn = new Button
        {
            CustomMinimumSize = new Vector2(PortraitWidth, PortraitHeight),
            Flat = true,
            FocusMode = Control.FocusModeEnum.None,
            ToggleMode = false,
            ClipContents = true,
        };

        // Background tinted from entity id so each colonist reads as a
        // distinct portrait without needing per-colonist art yet.
        var bg = new ColorRect
        {
            Color = TintFromId(entityId),
            AnchorLeft = 0f, AnchorRight = 1f, AnchorTop = 0f, AnchorBottom = 1f,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        btn.AddChild(bg);

        var viewportContainer = new SubViewportContainer
        {
            Stretch = true,
            AnchorLeft = 0f, AnchorRight = 1f, AnchorTop = 0f, AnchorBottom = 1f,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        var viewport = new SubViewport
        {
            Size = new Vector2I(PortraitWidth, PortraitHeight),
            TransparentBg = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            OwnWorld3D = true,
            World3D = new World3D(),
        };
        viewportContainer.AddChild(viewport);
        btn.AddChild(viewportContainer);

        // Body: same capsule the world renderer uses, framed by a small
        // perspective camera. Lighting via a single directional light
        // keyed to the front so silhouettes read clean.
        var body = new MeshInstance3D
        {
            Mesh = new CapsuleMesh
            {
                Radius = CapsuleRadiusMeters * UnitsPerMeter,
                Height = CapsuleHeightMeters * UnitsPerMeter,
                RadialSegments = 12,
                Rings = 4,
                Material = new StandardMaterial3D
                {
                    AlbedoColor = new Color(0.95f, 0.85f, 0.55f),
                    Roughness = 0.7f,
                },
            },
        };
        viewport.AddChild(body);

        var halfHeightUnits = CapsuleHeightMeters * 0.5f * UnitsPerMeter;
        // Default Camera3D forward is -Z, so a position offset on +Z with no
        // rotation already frames the body at the origin. Avoid Node3D.LookAt
        // here — it requires the camera to be inside the tree, but we
        // configure it before AddChild.
        var camera = new Camera3D
        {
            Position = new Vector3(0f, halfHeightUnits, 80f),
            Current = true,
            Fov = 28f,
        };
        viewport.AddChild(camera);

        var light = new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-30f, 30f, 0f),
            LightEnergy = 1.4f,
        };
        viewport.AddChild(light);

        // Selection highlight — a Panel with stylebox border on top of
        // everything else. Visibility flips in UpdateHighlights.
        var highlight = new Panel
        {
            AnchorLeft = 0f, AnchorRight = 1f, AnchorTop = 0f, AnchorBottom = 1f,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
        };
        var hlStyle = new StyleBoxFlat
        {
            BgColor = new Color(0, 0, 0, 0),
            BorderColor = new Color(1f, 0.95f, 0.5f),
            BorderWidthLeft = 3, BorderWidthRight = 3, BorderWidthTop = 3, BorderWidthBottom = 3,
        };
        highlight.AddThemeStyleboxOverride("panel", hlStyle);
        btn.AddChild(highlight);

        var slot = new PortraitSlot { EntityId = entityId, Button = btn, HighlightFrame = highlight };
        var captureId = entityId;
        btn.Pressed += () => OnPortraitPressed(captureId);
        return slot;
    }

    private void OnPortraitPressed(int entityId)
    {
        var snap = _publisher.Current;
        for (var i = 0; i < snap.Colonists.Count; i++)
        {
            var c = snap.Colonists[i];
            if (c.EntityId != entityId) continue;
            _cameraRig.FocusOnUnits(c.MetersX * UnitsPerMeter, c.MetersY * UnitsPerMeter);
            _selection.SelectColonist(entityId);
            return;
        }
    }

    private void UpdateHighlights()
    {
        var sel = _selection.SelectedEntityId;
        for (var i = 0; i < _slots.Count; i++)
        {
            _slots[i].HighlightFrame.Visible = sel.HasValue && _slots[i].EntityId == sel.Value;
        }
    }

    // Cheap deterministic hue from entity id so each colonist's portrait
    // background reads as theirs across reloads. Saturation/value are
    // fixed so contrast against the body stays predictable.
    private static Color TintFromId(int id)
    {
        unchecked
        {
            var h = (uint)id * 2654435761u;
            var hue = (h % 360u) / 360f;
            return Color.FromHsv(hue, 0.45f, 0.35f);
        }
    }
}
