using CowColonySim.Game.Selection;
using CowColonySim.Sim.Blueprints;
using CowColonySim.Sim.Snapshots;
using CowColonySim.Sim.World.Components;
using Godot;

namespace CowColonySim.Game.UI;

// Read-only readout that pops up when the player selects a built power
// pylon. Mirrors GeneratorPanel layout (top-right) but has no controls —
// pylons are pure relays. Shows grid id/status, supply vs demand, pylon
// + sink count, so the player can spot brownouts at a glance without
// hunting for a generator on the same grid.
public partial class PylonInfoPanel : CanvasLayer
{
    private SelectionService _selection = null!;
    private SnapshotPublisher _publisher = null!;

    private Control _root = null!;
    private Label _title = null!;
    private Label _statusLabel = null!;

    private int? _trackedEntityId;

    public void Configure(SelectionService selection, SnapshotPublisher publisher)
    {
        _selection = selection;
        _publisher = publisher;
    }

    public override void _Ready()
    {
        Layer = 25;

        _root = new Control
        {
            AnchorLeft = 1f, AnchorRight = 1f,
            AnchorTop = 0f, AnchorBottom = 0f,
            OffsetLeft = -340f, OffsetRight = -16f,
            OffsetTop = 16f, OffsetBottom = 160f,
            MouseFilter = Control.MouseFilterEnum.Stop,
            Visible = false,
        };
        AddChild(_root);

        var bg = new PanelContainer
        {
            AnchorLeft = 0f, AnchorRight = 1f,
            AnchorTop = 0f, AnchorBottom = 1f,
        };
        bg.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.07f, 0.07f, 0.09f, 0.92f),
            BorderColor = new Color(0.35f, 0.35f, 0.4f),
            BorderWidthTop = 1, BorderWidthBottom = 1, BorderWidthLeft = 1, BorderWidthRight = 1,
            ContentMarginLeft = 12, ContentMarginRight = 12,
            ContentMarginTop = 10, ContentMarginBottom = 10,
        });
        _root.AddChild(bg);

        var col = new VBoxContainer();
        bg.AddChild(col);

        _title = new Label { Text = "Power Pylon" };
        col.AddChild(_title);

        _statusLabel = new Label { Text = "grid: —" };
        col.AddChild(_statusLabel);

        _selection.SelectionChanged += OnSelectionChanged;
    }

    private void OnSelectionChanged()
    {
        var newId = ResolvePylonId();
        _trackedEntityId = newId;
        _root.Visible = newId.HasValue;
        if (newId.HasValue) RefreshFromSnapshot();
    }

    private int? ResolvePylonId()
    {
        if (_selection.SelectedStructureId is not int id) return null;
        var snap = _publisher.Current;
        for (var i = 0; i < snap.Structures.Count; i++)
        {
            if (snap.Structures[i].EntityId != id) continue;
            if (!BlueprintCatalog.TryGet(snap.Structures[i].DefId, out var def) || def is null) return null;
            if (def.Power == PowerNodeKind.Pylon) return id;
            return null;
        }
        return null;
    }

    public override void _Process(double delta)
    {
        if (_trackedEntityId is null) return;
        RefreshFromSnapshot();
    }

    private void RefreshFromSnapshot()
    {
        if (_trackedEntityId is not int id) return;
        var snap = _publisher.Current;
        PowerNodeView? node = null;
        for (var i = 0; i < snap.PowerNodes.Count; i++)
        {
            if (snap.PowerNodes[i].EntityId == id) { node = snap.PowerNodes[i]; break; }
        }
        if (node is null) { _root.Visible = false; return; }
        var n = node.Value;

        var status = "no grid (isolated)";
        if (n.GridId >= 0)
        {
            for (var i = 0; i < snap.PowerGrids.Count; i++)
            {
                if (snap.PowerGrids[i].Id != n.GridId) continue;
                var g = snap.PowerGrids[i];
                status = $"grid #{g.Id} {g.Status}\nsupply {g.TotalSupplyW:F0} W / demand {g.TotalDemandW:F0} W\npylons {g.PylonCount} • sources {g.SourceCount} • sinks {g.SinkCount}";
                break;
            }
        }
        _statusLabel.Text = status;
    }
}
