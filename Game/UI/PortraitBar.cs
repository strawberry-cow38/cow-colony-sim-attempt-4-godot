using CowColonySim.Game.Camera;
using CowColonySim.Game.Selection;
using CowColonySim.Sim;
using CowColonySim.Sim.Snapshots;
using Godot;

namespace CowColonySim.Game.UI;

// Top-center bar of one portrait per colonist. Each portrait is a tiny
// SubViewport rendering a 3D body (capsule today, clothed mesh later)
// over a solid dark gray background, framed in a Button so clicks focus
// the camera + select the colonist. The portrait box has a colored
// border keyed to the colonist's mood (placeholder until mood data is
// wired). Below each portrait is a name label. Portraits are rebuilt
// only when the colonist roster changes — per-frame work just moves the
// camera + updates the border + selection ring.
public partial class PortraitBar : CanvasLayer
{
    private const int PortraitWidth = 88;
    private const int PortraitHeight = 112;
    private const float CapsuleRadiusMeters = 0.25f;
    private const float CapsuleHeightMeters = 1.7f;
    private const float UnitsPerMeter = SimConstants.GodotUnitsPerTile / SimConstants.MetersPerTile;
    private static readonly Color BackgroundGray = new(0.16f, 0.16f, 0.18f);

    // Placeholder name pool used until colonists carry real names. Pick
    // deterministically by entity id so the same colonist always reads
    // with the same label across reloads.
    private static readonly string[] PlaceholderNames =
    {
        "Aki", "Bex", "Cal", "Dro", "Ena", "Fen", "Gus", "Hao",
        "Iri", "Jun", "Kio", "Lev", "Mio", "Nyx", "Ona", "Pip",
    };

    private SelectionService _selection = null!;
    private SnapshotPublisher _publisher = null!;
    private CameraRig _cameraRig = null!;

    private HBoxContainer _bar = null!;
    private readonly List<PortraitSlot> _slots = new();
    // Entity currently locked to the camera via portrait double-click.
    // Kept until the rig itself reports IsFollowing=false (e.g. WASD
    // broke the lock), at which point we forget it.
    private int? _followingEntityId;

    private sealed class PortraitSlot
    {
        public int EntityId;
        public Control Root = null!;
        public Button Button = null!;
        public Panel BorderFrame = null!;
        public StyleBoxFlat BorderStyle = null!;
        public Panel SelectionRing = null!;
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
            OffsetLeft = 0f, OffsetRight = 0f, OffsetTop = 8f, OffsetBottom = PortraitHeight + 48f,
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
        DriveFollow(snap);
    }

    private void DriveFollow(SimSnapshot snap)
    {
        if (_followingEntityId is not int id) return;
        if (!_cameraRig.IsFollowing)
        {
            _followingEntityId = null;
            return;
        }
        for (var i = 0; i < snap.Colonists.Count; i++)
        {
            var c = snap.Colonists[i];
            if (c.EntityId != id) continue;
            _cameraRig.FollowAt(c.MetersX * UnitsPerMeter, c.MetersY * UnitsPerMeter);
            return;
        }
        // Target gone (despawned) — forget it.
        _followingEntityId = null;
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

        foreach (var slot in _slots) slot.Root.QueueFree();
        _slots.Clear();
        foreach (var child in _bar.GetChildren()) child.QueueFree();

        for (var i = 0; i < colonists.Count; i++)
        {
            var c = colonists[i];
            var slot = BuildSlot(c.EntityId);
            _slots.Add(slot);
            _bar.AddChild(slot.Root);
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
        // Each slot stacks: portrait button on top, name label under it.
        var column = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(PortraitWidth, PortraitHeight + 24f),
        };
        column.AddThemeConstantOverride("separation", 4);

        var btn = new Button
        {
            CustomMinimumSize = new Vector2(PortraitWidth, PortraitHeight),
            Flat = true,
            FocusMode = Control.FocusModeEnum.None,
            ToggleMode = false,
            ClipContents = true,
        };
        column.AddChild(btn);

        // Solid dark gray background — single color across all portraits;
        // identity comes from the body itself + name + mood border, not the
        // background tint.
        var bg = new ColorRect
        {
            Color = BackgroundGray,
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
        // Portrait body is static — rendering every frame just burns GPU
        // and tanks framerate once the colony grows. UpdateOnce paints the
        // viewport one time, then it sits as a cached texture. Anywhere
        // we change visual state (mood color, body swap, future
        // animations) call viewport.RenderTargetUpdateMode = UpdateOnce
        // to repaint a single frame.
        var viewport = new SubViewport
        {
            Size = new Vector2I(PortraitWidth, PortraitHeight),
            TransparentBg = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Once,
            OwnWorld3D = true,
            World3D = new World3D(),
        };
        viewportContainer.AddChild(viewport);
        btn.AddChild(viewportContainer);

        // Body: same capsule the world renderer uses, framed by a small
        // perspective camera. Lighting via a single directional light
        // keyed to the front so silhouettes read clean.
        var halfHeightUnits = CapsuleHeightMeters * 0.5f * UnitsPerMeter;
        // Translate the capsule up so its feet sit on y=0 instead of straddling
        // the origin. Otherwise the frame center lands mid-torso and the lower
        // body falls off the bottom of the SubViewport.
        var body = new MeshInstance3D
        {
            Position = new Vector3(0f, halfHeightUnits, 0f),
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
        // Default Camera3D forward is -Z, so a position offset on +Z with no
        // rotation already frames the body at the origin. Distance + Fov
        // chosen so the full ~73u capsule fits with margin top and bottom.
        // Distance pulled in twice (10% each pass) from the original framing
        // to make the body read bigger inside the portrait box. Keep Fov
        // fixed so the perspective stays consistent.
        var camera = new Camera3D
        {
            Position = new Vector3(0f, halfHeightUnits, 115f),
            Current = true,
            Fov = 36f,
        };
        viewport.AddChild(camera);

        // No shadows on portraits — single-light + capsule body doesn't
        // benefit, and shadow maps per portrait viewport were a real
        // GPU cost as the roster grew.
        var light = new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-30f, 30f, 0f),
            LightEnergy = 1.4f,
            ShadowEnabled = false,
        };
        viewport.AddChild(light);

        // Mood-color border lives on top of the portrait. Style is rebuilt
        // each frame in UpdateBorders to track the colonist's current mood
        // (placeholder until mood data is wired). The selection ring is a
        // separate panel layered above so it doesn't clobber the mood color.
        var border = new Panel
        {
            AnchorLeft = 0f, AnchorRight = 1f, AnchorTop = 0f, AnchorBottom = 1f,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        var borderStyle = new StyleBoxFlat
        {
            BgColor = new Color(0, 0, 0, 0),
            BorderColor = MoodPlaceholderColor(entityId),
            BorderWidthLeft = 3, BorderWidthRight = 3, BorderWidthTop = 3, BorderWidthBottom = 3,
        };
        border.AddThemeStyleboxOverride("panel", borderStyle);
        btn.AddChild(border);

        var ring = new Panel
        {
            AnchorLeft = 0f, AnchorRight = 1f, AnchorTop = 0f, AnchorBottom = 1f,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
        };
        var ringStyle = new StyleBoxFlat
        {
            BgColor = new Color(0, 0, 0, 0),
            BorderColor = new Color(1f, 0.95f, 0.5f),
            BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
        };
        ring.AddThemeStyleboxOverride("panel", ringStyle);
        btn.AddChild(ring);

        var nameLabel = new Label
        {
            Text = NameForId(entityId),
            HorizontalAlignment = HorizontalAlignment.Center,
            CustomMinimumSize = new Vector2(PortraitWidth, 0f),
        };
        nameLabel.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f));
        nameLabel.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f));
        nameLabel.AddThemeConstantOverride("outline_size", 4);
        nameLabel.AddThemeFontSizeOverride("font_size", 13);
        column.AddChild(nameLabel);

        var slot = new PortraitSlot
        {
            EntityId = entityId,
            Root = column,
            Button = btn,
            BorderFrame = border,
            BorderStyle = borderStyle,
            SelectionRing = ring,
        };
        var captureId = entityId;
        // Use GuiInput rather than Pressed so we can tell single-click from
        // double-click. Single = just select. Double = focus + lock follow.
        btn.GuiInput += ev => OnPortraitGuiInput(captureId, ev);
        return slot;
    }

    private void OnPortraitGuiInput(int entityId, InputEvent ev)
    {
        if (ev is not InputEventMouseButton mb) return;
        if (!mb.Pressed || mb.ButtonIndex != MouseButton.Left) return;

        if (mb.DoubleClick)
        {
            FocusAndFollow(entityId);
        }
        else
        {
            _selection.SelectColonist(entityId);
        }
    }

    private void FocusAndFollow(int entityId)
    {
        var snap = _publisher.Current;
        for (var i = 0; i < snap.Colonists.Count; i++)
        {
            var c = snap.Colonists[i];
            if (c.EntityId != entityId) continue;
            _selection.SelectColonist(entityId);
            _cameraRig.BeginFollow();
            _cameraRig.FollowAt(c.MetersX * UnitsPerMeter, c.MetersY * UnitsPerMeter);
            _followingEntityId = entityId;
            return;
        }
    }

    private void UpdateHighlights()
    {
        var sel = _selection.SelectedEntityId;
        for (var i = 0; i < _slots.Count; i++)
        {
            _slots[i].SelectionRing.Visible = sel.HasValue && _slots[i].EntityId == sel.Value;
        }
    }

    // Placeholder mood color until colonists carry real mood data. Hue is
    // deterministic per entity id so portraits read distinctly during dev,
    // but this whole function should be replaced with a snap-driven mood
    // lookup once moods are wired (red for angry, green for content, etc).
    private static Color MoodPlaceholderColor(int id)
    {
        unchecked
        {
            var h = (uint)id * 2654435761u;
            var hue = (h % 360u) / 360f;
            return Color.FromHsv(hue, 0.55f, 0.95f);
        }
    }

    private static string NameForId(int id)
    {
        unchecked
        {
            var h = (uint)id * 2654435761u;
            return PlaceholderNames[h % (uint)PlaceholderNames.Length];
        }
    }
}
