using CowColonySim.Game.Selection;
using CowColonySim.Sim.Commands;
using CowColonySim.Sim.Designations;
using CowColonySim.Sim.Snapshots;
using CowColonySim.Sim.Zones;
using Godot;

namespace CowColonySim.Game.UI;

// Right-click context menu for entities in the world. Currently only
// trees: shows "prioritize chop (selected colonist)" when a colonist is
// selected, plus designate/cancel chop. Submits through CommandBus —
// nothing here mutates the sim directly. SelectionService decides when
// to call OpenForTree.
public partial class ContextMenu : CanvasLayer
{
    private SelectionService _selection = null!;
    private SnapshotPublisher _publisher = null!;
    private CommandBus _commands = null!;
    private PopupMenu _menu = null!;

    private enum Action
    {
        PrioritizeChop = 1,
        DesignateChop,
        CancelChop,
    }

    private int _treeId;
    private int _treeTileX;
    private int _treeTileY;
    private int _colonistId;

    public void Configure(SelectionService selection, SnapshotPublisher publisher, CommandBus commands)
    {
        _selection = selection;
        _publisher = publisher;
        _commands = commands;
    }

    public override void _Ready()
    {
        Layer = 110;
        _menu = new PopupMenu { Name = "ContextPopup" };
        _menu.IdPressed += OnIdPressed;
        AddChild(_menu);
    }

    public void OpenForTree(int treeId, Vector2 screenPos)
    {
        var snap = _publisher.Current;
        TreeView? tree = null;
        for (var i = 0; i < snap.Trees.Count; i++)
        {
            if (snap.Trees[i].EntityId != treeId) continue;
            tree = snap.Trees[i];
            break;
        }
        if (tree is null) return;

        _treeId = treeId;
        _treeTileX = tree.Value.TileX;
        _treeTileY = tree.Value.TileY;
        _colonistId = _selection.SelectedEntityId ?? 0;

        _menu.Clear();
        if (_colonistId != 0)
        {
            _menu.AddItem($"prioritize chop (colonist #{_colonistId})", (int)Action.PrioritizeChop);
        }
        var designated = HasChopDesignation(snap, _treeTileX, _treeTileY);
        if (!designated) _menu.AddItem("designate chop", (int)Action.DesignateChop);
        else _menu.AddItem("cancel chop", (int)Action.CancelChop);

        var pos = (Vector2I)screenPos;
        _menu.Position = pos;
        _menu.ResetSize();
        _menu.Popup();
    }

    private void OnIdPressed(long id)
    {
        switch ((Action)id)
        {
            case Action.PrioritizeChop:
                if (_colonistId == 0 || _treeId == 0) return;
                _commands.Submit(new PrioritizeChopCommand(_colonistId, _treeId));
                break;
            case Action.DesignateChop:
                _commands.Submit(new StampDesignationsCommand(
                    DesignationKind.ChopTree,
                    new TileRect(_treeTileX, _treeTileY, _treeTileX, _treeTileY)));
                break;
            case Action.CancelChop:
                _commands.Submit(new EraseInRectCommand(
                    new TileRect(_treeTileX, _treeTileY, _treeTileX, _treeTileY)));
                break;
        }
    }

    private static bool HasChopDesignation(SimSnapshot snap, int tx, int ty)
    {
        for (var i = 0; i < snap.Designations.Count; i++)
        {
            var d = snap.Designations[i];
            if (d.Kind != DesignationKind.ChopTree) continue;
            if (d.TileX == tx && d.TileY == ty) return true;
        }
        return false;
    }
}
