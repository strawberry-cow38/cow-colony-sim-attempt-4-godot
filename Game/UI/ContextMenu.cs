using CowColonySim.Game.Selection;
using CowColonySim.Sim.Commands;
using CowColonySim.Sim.Designations;
using CowColonySim.Sim.Items;
using CowColonySim.Sim.Snapshots;
using CowColonySim.Sim.Zones;
using Godot;

namespace CowColonySim.Game.UI;

// Right-click context menu for entities in the world. Custom popup
// (PanelContainer + Button list) instead of Godot's PopupMenu so each
// entry can react to BOTH left- and right-click — the user wants
// either button to fire the option, since that's the same gesture
// they used to open the menu.
//
// SelectionService decides when to call OpenForTree / OpenForItem.
// All actions submit through CommandBus — never mutate the sim here.
public partial class ContextMenu : CanvasLayer
{
    private SelectionService _selection = null!;
    private SnapshotPublisher _publisher = null!;
    private CommandBus _commands = null!;

    private Control _root = null!;
    private PanelContainer _panel = null!;
    private VBoxContainer _items = null!;
    private Control _dismissCatcher = null!;

    public void Configure(SelectionService selection, SnapshotPublisher publisher, CommandBus commands)
    {
        _selection = selection;
        _publisher = publisher;
        _commands = commands;
    }

    public override void _Ready()
    {
        Layer = 110;
        _root = new Control
        {
            Name = "ContextRoot",
            AnchorRight = 1f,
            AnchorBottom = 1f,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
        };
        AddChild(_root);

        // Full-viewport invisible button under the panel so any click
        // outside the menu dismisses it. Sits behind the panel in z-order
        // because we add the panel after.
        _dismissCatcher = new Control
        {
            Name = "DismissCatcher",
            AnchorRight = 1f,
            AnchorBottom = 1f,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        _dismissCatcher.GuiInput += OnDismissInput;
        _root.AddChild(_dismissCatcher);

        _panel = new PanelContainer { Name = "ContextPanel" };
        _root.AddChild(_panel);

        _items = new VBoxContainer { Name = "Items" };
        _panel.AddChild(_items);
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
        var tx = tree.Value.TileX;
        var ty = tree.Value.TileY;
        var colonistId = _selection.SelectedEntityId ?? 0;

        ClearItems();
        if (colonistId != 0)
        {
            AddOption($"prioritize chop (colonist #{colonistId})",
                () => _commands.Submit(new PrioritizeChopCommand(colonistId, treeId)));
        }
        if (HasChopDesignation(snap, tx, ty))
        {
            AddOption("cancel chop",
                () => _commands.Submit(new EraseInRectCommand(new TileRect(tx, ty, tx, ty))));
        }
        else
        {
            AddOption("designate chop",
                () => _commands.Submit(new StampDesignationsCommand(
                    DesignationKind.ChopTree, new TileRect(tx, ty, tx, ty))));
        }
        Show(screenPos);
    }

    public void OpenForBoulder(int boulderId, Vector2 screenPos)
    {
        var snap = _publisher.Current;
        BoulderView? boulder = null;
        for (var i = 0; i < snap.Boulders.Count; i++)
        {
            if (snap.Boulders[i].EntityId != boulderId) continue;
            boulder = snap.Boulders[i];
            break;
        }
        if (boulder is null) return;
        var tx = boulder.Value.TileX;
        var ty = boulder.Value.TileY;
        var colonistId = _selection.SelectedEntityId ?? 0;

        ClearItems();
        if (HasMineDesignation(snap, tx, ty))
        {
            AddOption("cancel mine",
                () => _commands.Submit(new EraseInRectCommand(new TileRect(tx, ty, tx, ty))));
        }
        else
        {
            AddOption("designate mine",
                () => _commands.Submit(new StampDesignationsCommand(
                    DesignationKind.Mine, new TileRect(tx, ty, tx, ty))));
        }
        Show(screenPos);
    }

    public void OpenForItem(int itemId, Vector2 screenPos)
    {
        var snap = _publisher.Current;
        ItemView? item = null;
        for (var i = 0; i < snap.Items.Count; i++)
        {
            if (snap.Items[i].EntityId != itemId) continue;
            item = snap.Items[i];
            break;
        }
        if (item is null) return;
        var view = item.Value;
        var colonistId = _selection.SelectedEntityId ?? 0;

        ClearItems();
        if (colonistId != 0 && !view.Forbidden)
        {
            AddOption($"prioritize haul (colonist #{colonistId})",
                () => _commands.Submit(new PrioritizeHaulCommand(colonistId, itemId)));
            AddOption($"force pickup (colonist #{colonistId})",
                () => _commands.Submit(new ForcePickupCommand(colonistId, itemId)));
        }
        if (view.Forbidden)
        {
            AddOption("unforbid",
                () => _commands.Submit(new SetItemForbiddenCommand(itemId, false)));
        }
        else
        {
            AddOption("forbid",
                () => _commands.Submit(new SetItemForbiddenCommand(itemId, true)));
        }
        Show(screenPos);
    }

    private void Show(Vector2 screenPos)
    {
        _root.Visible = true;
        _panel.Position = screenPos;
        _panel.ResetSize();
    }

    private void Close() => _root.Visible = false;

    public void CloseIfOpen()
    {
        if (_root.Visible) _root.Visible = false;
    }

    private void ClearItems()
    {
        foreach (var child in _items.GetChildren())
        {
            child.QueueFree();
        }
    }

    private void AddOption(string label, System.Action action)
    {
        var btn = new Button
        {
            Text = label,
            CustomMinimumSize = new Vector2(220f, 24f),
            FocusMode = Control.FocusModeEnum.None,
        };
        // Both left and right clicks fire the action. The user opened the
        // menu with right-click, so making them switch hands to commit a
        // choice is hostile. Listen on gui_input so we see the raw button.
        btn.GuiInput += (InputEvent ev) =>
        {
            if (ev is not InputEventMouseButton mb || !mb.Pressed) return;
            if (mb.ButtonIndex != MouseButton.Left && mb.ButtonIndex != MouseButton.Right) return;
            _root.GetViewport().SetInputAsHandled();
            action.Invoke();
            Close();
        };
        _items.AddChild(btn);
    }

    private void OnDismissInput(InputEvent ev)
    {
        if (ev is not InputEventMouseButton mb || !mb.Pressed) return;
        Close();
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

    private static bool HasMineDesignation(SimSnapshot snap, int tx, int ty)
    {
        for (var i = 0; i < snap.Designations.Count; i++)
        {
            var d = snap.Designations[i];
            if (d.Kind != DesignationKind.Mine) continue;
            if (d.TileX == tx && d.TileY == ty) return true;
        }
        return false;
    }
}
