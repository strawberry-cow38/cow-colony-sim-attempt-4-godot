using Godot;

namespace CowColonySim.Game.UI;

// Translucent 2D box drawn over the screen while the player drags a
// selection rect with LMB. Owner sets PreviewRect each frame and clears
// it on commit/cancel. Lives in screen space so it can sit on top of the
// portrait bar — the player can lasso world colonists OR portraits in the
// same gesture.
public partial class ScreenSelectionOverlay : CanvasLayer
{
    private static readonly Color FillColor = new(0.20f, 0.85f, 0.50f, 0.18f);
    private static readonly Color BorderColor = new(0.55f, 1f, 0.65f, 0.85f);

    private RectControl _rect = null!;

    public Rect2? PreviewRect
    {
        get => _rect.PreviewRect;
        set => _rect.PreviewRect = value;
    }

    public override void _Ready()
    {
        // Above world overlays, below modal panels (info/context = layer 10+).
        Layer = 8;
        _rect = new RectControl
        {
            AnchorLeft = 0f, AnchorRight = 1f, AnchorTop = 0f, AnchorBottom = 1f,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        AddChild(_rect);
    }

    private partial class RectControl : Control
    {
        private Rect2? _previewRect;
        public Rect2? PreviewRect
        {
            get => _previewRect;
            set
            {
                if (_previewRect == value) return;
                _previewRect = value;
                QueueRedraw();
            }
        }

        public override void _Draw()
        {
            if (_previewRect is not Rect2 r) return;
            DrawRect(r, FillColor, filled: true);
            DrawRect(r, BorderColor, filled: false, width: 1f);
        }
    }
}
