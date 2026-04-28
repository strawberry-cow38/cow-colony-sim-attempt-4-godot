using CowColonySim.Game.Selection;
using CowColonySim.Sim.Blueprints;
using CowColonySim.Sim.Commands;
using CowColonySim.Sim.Designations;
using CowColonySim.Sim.Items;
using CowColonySim.Sim.Zones;
using CowColonySim.Sim.Snapshots;
using CowColonySim.Sim.World.Components;
using Godot;

namespace CowColonySim.Game.UI;

// Bottom-left panel. Renders different sub-panels per selection kind:
// colonist (needs + job), tree (health + chop actions). Only one
// sub-panel is visible at a time. Buttons submit through CommandBus —
// the panel never mutates the sim directly.
public partial class InfoPanel : CanvasLayer
{
    private SelectionService _selection = null!;
    private SnapshotPublisher _publisher = null!;
    private CommandBus _commands = null!;

    private VBoxContainer _colonistBox = null!;
    private Label _colonistHeader = null!;
    private ProgressBar _hungerBar = null!;
    private ProgressBar _thirstBar = null!;
    private ProgressBar _energyBar = null!;
    private Label _jobLabel = null!;
    private Label _weightLabel = null!;
    private ProgressBar _weightBar = null!;
    private Label _bulkLabel = null!;
    private ProgressBar _bulkBar = null!;
    private VBoxContainer _inventoryList = null!;
    private AcceptDialog _itemInfoDialog = null!;
    private int _lastInvColonistId;
    private ulong _lastInvSig;

    private VBoxContainer _treeBox = null!;
    private Label _treeHeader = null!;
    private ProgressBar _treeHealthBar = null!;
    private Button _designateChopBtn = null!;
    private Button _cancelChopBtn = null!;

    private VBoxContainer _itemBox = null!;
    private Label _itemHeader = null!;
    private Label _itemDescription = null!;
    private CheckBox _forbidCheck = null!;

    private VBoxContainer _blueprintBox = null!;
    private Label _blueprintHeader = null!;
    private Label _blueprintMaterials = null!;
    private ProgressBar _blueprintProgressBar = null!;
    private Button _cancelBlueprintBtn = null!;

    private VBoxContainer _structureBox = null!;
    private Label _structureHeader = null!;
    private Button _uninstallBtn = null!;
    private Button _deconstructBtn = null!;

    private Label _emptyLabel = null!;
    private bool _forbidSyncing;

    public void Configure(SelectionService selection, SnapshotPublisher publisher, CommandBus commands)
    {
        _selection = selection;
        _publisher = publisher;
        _commands = commands;
    }

    public override void _Ready()
    {
        Layer = 100;
        var panel = new PanelContainer
        {
            Position = new Vector2(8f, 0f),
            AnchorTop = 1f,
            AnchorBottom = 1f,
            OffsetTop = -440f,
            OffsetBottom = -8f,
            CustomMinimumSize = new Vector2(320f, 430f),
        };
        AddChild(panel);

        var root = new VBoxContainer();
        panel.AddChild(root);

        _emptyLabel = MakeLabel("no selection\nleft-click colonist or tree · right-click ground to move");
        root.AddChild(_emptyLabel);

        _colonistBox = new VBoxContainer { Visible = false };
        root.AddChild(_colonistBox);
        _colonistHeader = MakeLabel("colonist");
        _colonistBox.AddChild(_colonistHeader);
        _hungerBar = MakeBar(new Color(0.3f, 0.85f, 0.35f));
        _thirstBar = MakeBar(new Color(0.3f, 0.55f, 0.95f));
        _energyBar = MakeBar(new Color(0.95f, 0.85f, 0.25f));
        _colonistBox.AddChild(MakeLabel("hunger"));
        _colonistBox.AddChild(_hungerBar);
        _colonistBox.AddChild(MakeLabel("thirst"));
        _colonistBox.AddChild(_thirstBar);
        _colonistBox.AddChild(MakeLabel("energy"));
        _colonistBox.AddChild(_energyBar);
        _jobLabel = MakeLabel("job: idle");
        _colonistBox.AddChild(_jobLabel);

        _weightLabel = MakeLabel("weight 0 / 0 kg");
        _colonistBox.AddChild(_weightLabel);
        _weightBar = MakeBar(new Color(0.85f, 0.55f, 0.25f));
        _colonistBox.AddChild(_weightBar);
        _bulkLabel = MakeLabel("bulk 0 / 0 L");
        _colonistBox.AddChild(_bulkLabel);
        _bulkBar = MakeBar(new Color(0.55f, 0.45f, 0.85f));
        _colonistBox.AddChild(_bulkBar);

        _colonistBox.AddChild(MakeLabel("inventory"));
        var invScroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0f, 140f),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        _colonistBox.AddChild(invScroll);
        _inventoryList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        invScroll.AddChild(_inventoryList);

        _itemInfoDialog = new AcceptDialog { Title = "item" };
        AddChild(_itemInfoDialog);

        _treeBox = new VBoxContainer { Visible = false };
        root.AddChild(_treeBox);
        _treeHeader = MakeLabel("pine");
        _treeBox.AddChild(_treeHeader);
        _treeBox.AddChild(MakeLabel("health"));
        _treeHealthBar = MakeBar(new Color(0.4f, 0.8f, 0.35f));
        _treeBox.AddChild(_treeHealthBar);
        _designateChopBtn = new Button { Text = "designate chop" };
        _designateChopBtn.Pressed += OnDesignateChop;
        _treeBox.AddChild(_designateChopBtn);
        _cancelChopBtn = new Button { Text = "cancel chop" };
        _cancelChopBtn.Pressed += OnCancelChop;
        _treeBox.AddChild(_cancelChopBtn);

        _itemBox = new VBoxContainer { Visible = false };
        root.AddChild(_itemBox);
        _itemHeader = MakeLabel("item");
        _itemBox.AddChild(_itemHeader);
        _itemDescription = MakeLabel("");
        _itemDescription.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _itemBox.AddChild(_itemDescription);
        _forbidCheck = new CheckBox { Text = "forbid" };
        _forbidCheck.Toggled += OnForbidToggled;
        _itemBox.AddChild(_forbidCheck);

        _blueprintBox = new VBoxContainer { Visible = false };
        root.AddChild(_blueprintBox);
        _blueprintHeader = MakeLabel("blueprint");
        _blueprintBox.AddChild(_blueprintHeader);
        _blueprintMaterials = MakeLabel("0 / 0 wood");
        _blueprintBox.AddChild(_blueprintMaterials);
        _blueprintBox.AddChild(MakeLabel("build progress"));
        _blueprintProgressBar = MakeBar(new Color(0.4f, 0.7f, 0.95f));
        _blueprintBox.AddChild(_blueprintProgressBar);
        _cancelBlueprintBtn = new Button { Text = "cancel" };
        _cancelBlueprintBtn.Pressed += OnCancelBlueprint;
        _blueprintBox.AddChild(_cancelBlueprintBtn);

        _structureBox = new VBoxContainer { Visible = false };
        root.AddChild(_structureBox);
        _structureHeader = MakeLabel("structure");
        _structureBox.AddChild(_structureHeader);
        _uninstallBtn = new Button { Text = "uninstall" };
        _uninstallBtn.Pressed += OnUninstall;
        _structureBox.AddChild(_uninstallBtn);
        _deconstructBtn = new Button { Text = "deconstruct (returns half)" };
        _deconstructBtn.Pressed += OnDeconstruct;
        _structureBox.AddChild(_deconstructBtn);
    }

    private static Label MakeLabel(string text)
    {
        var l = new Label { Text = text };
        l.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f));
        l.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f));
        l.AddThemeConstantOverride("outline_size", 4);
        l.AddThemeFontSizeOverride("font_size", 13);
        return l;
    }

    private static ProgressBar MakeBar(Color fill)
    {
        var bar = new ProgressBar
        {
            MinValue = 0,
            MaxValue = 100,
            Value = 100,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(0f, 14f),
        };
        var fillStyle = new StyleBoxFlat { BgColor = fill };
        var bgStyle = new StyleBoxFlat { BgColor = new Color(0.15f, 0.15f, 0.18f) };
        bar.AddThemeStyleboxOverride("fill", fillStyle);
        bar.AddThemeStyleboxOverride("background", bgStyle);
        return bar;
    }

    public override void _Process(double delta)
    {
        var snap = _publisher.Current;
        if (_selection.SelectedEntityId is int colonistId)
        {
            ShowColonist(snap, colonistId);
            return;
        }
        if (_selection.SelectedTreeId is int treeId)
        {
            ShowTree(snap, treeId);
            return;
        }
        if (_selection.SelectedItemId is int itemId)
        {
            ShowItem(snap, itemId);
            return;
        }
        if (_selection.SelectedBlueprintId is int bpId)
        {
            ShowBlueprint(snap, bpId);
            return;
        }
        if (_selection.SelectedStructureId is int structId)
        {
            ShowStructure(snap, structId);
            return;
        }
        ShowEmpty();
    }

    private void ShowEmpty()
    {
        _emptyLabel.Visible = true;
        _colonistBox.Visible = false;
        _treeBox.Visible = false;
        _itemBox.Visible = false;
        _blueprintBox.Visible = false;
        _structureBox.Visible = false;
    }

    private void ShowBlueprint(SimSnapshot snap, int id)
    {
        _emptyLabel.Visible = false;
        _colonistBox.Visible = false;
        _treeBox.Visible = false;
        _itemBox.Visible = false;
        _structureBox.Visible = false;
        _blueprintBox.Visible = true;
        for (var i = 0; i < snap.BlueprintGhosts.Count; i++)
        {
            var g = snap.BlueprintGhosts[i];
            if (g.EntityId != id) continue;
            var name = BlueprintCatalog.TryGet(g.DefId, out var def) && def is not null
                ? def.DisplayName : g.DefId;
            _blueprintHeader.Text =
                $"{name} (blueprint)\n" +
                $"tile ({g.OriginTileX}, {g.OriginTileY})";
            _blueprintMaterials.Text = $"{g.MaterialDeposited} / {g.MaterialRequired} wood";
            _blueprintProgressBar.Value = Mathf.Clamp(g.BuildProgress * 100f, 0f, 100f);
            return;
        }
        _blueprintHeader.Text = $"blueprint #{id} (gone)";
        _blueprintMaterials.Text = "";
        _blueprintProgressBar.Value = 0;
    }

    private void ShowStructure(SimSnapshot snap, int id)
    {
        _emptyLabel.Visible = false;
        _colonistBox.Visible = false;
        _treeBox.Visible = false;
        _itemBox.Visible = false;
        _blueprintBox.Visible = false;
        _structureBox.Visible = true;
        for (var i = 0; i < snap.Structures.Count; i++)
        {
            var s = snap.Structures[i];
            if (s.EntityId != id) continue;
            var name = BlueprintCatalog.TryGet(s.DefId, out var def) && def is not null
                ? def.DisplayName : s.DefId;
            _structureHeader.Text =
                $"{name}\n" +
                $"tile ({s.TileX}, {s.TileY})";
            return;
        }
        _structureHeader.Text = $"structure #{id} (gone)";
    }

    private void OnCancelBlueprint()
    {
        if (_selection.SelectedBlueprintId is not int id) return;
        _commands.Submit(new CancelBlueprintCommand(id));
    }

    private void OnUninstall()
    {
        if (_selection.SelectedStructureId is not int id) return;
        _commands.Submit(new UninstallStructureCommand(id));
    }

    private void OnDeconstruct()
    {
        if (_selection.SelectedStructureId is not int id) return;
        _commands.Submit(new DeconstructStructureCommand(id));
    }

    private void ShowColonist(SimSnapshot snap, int id)
    {
        _emptyLabel.Visible = false;
        _treeBox.Visible = false;
        _itemBox.Visible = false;
        _blueprintBox.Visible = false;
        _structureBox.Visible = false;
        _colonistBox.Visible = true;
        for (var i = 0; i < snap.Colonists.Count; i++)
        {
            var c = snap.Colonists[i];
            if (c.EntityId != id) continue;
            _colonistHeader.Text =
                $"colonist #{id}\n" +
                $"pos: ({c.MetersX:F1}m, {c.MetersY:F1}m)";
            _hungerBar.Value = c.Hunger;
            _thirstBar.Value = c.Thirst;
            _energyBar.Value = c.Energy;
            var jobText = c.JobActive
                ? $"job: satisfy {KindName(c.JobKind)}"
                : c.WorkActive
                    ? $"job: {WorkLabel(c.WorkKind)}"
                    : "job: idle";
            if (c.Carrying && c.CarryCount > 0)
            {
                jobText += $"\ncarrying: {KindLabel(c.CarryKind)} ×{c.CarryCount}";
            }
            _jobLabel.Text = jobText;

            _weightLabel.Text = $"weight {c.CarryWeight:F1} / {c.MaxWeight:F1} kg";
            _weightBar.MaxValue = Mathf.Max(0.001, c.MaxWeight);
            _weightBar.Value = c.CarryWeight;
            _bulkLabel.Text = $"bulk {c.CarryBulk:F1} / {c.MaxBulk:F1} L";
            _bulkBar.MaxValue = Mathf.Max(0.001, c.MaxBulk);
            _bulkBar.Value = c.CarryBulk;
            RebuildInventoryList(id, c.Inventory);
            return;
        }
        _colonistHeader.Text = $"colonist #{id} (offline)";
    }

    private void RebuildInventoryList(int colonistId, IReadOnlyList<InventoryStackView> inv)
    {
        // Buttons are rebuilt only when contents change. _Process runs at
        // 60Hz; QueueFree-ing buttons every frame races click handling
        // (press lands on instance N, instance N+1 sees the release).
        var sig = ComputeInvSig(inv);
        if (colonistId == _lastInvColonistId && sig == _lastInvSig) return;
        _lastInvColonistId = colonistId;
        _lastInvSig = sig;

        foreach (var child in _inventoryList.GetChildren())
        {
            child.QueueFree();
        }
        if (inv is null || inv.Count == 0)
        {
            _inventoryList.AddChild(MakeLabel("(empty)"));
            return;
        }
        for (var i = 0; i < inv.Count; i++)
        {
            var stack = inv[i];
            var row = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            var tag = stack.Equipped ? " [E]" : stack.Locked ? " [L]" : string.Empty;
            var displayName = !string.IsNullOrEmpty(stack.WrappedDefId)
                ? MinifiedLabel(stack.WrappedDefId) : stack.DisplayName;
            var nameLabel = MakeLabel($"{displayName} ×{stack.Count}{tag}");
            nameLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(nameLabel);

            if (stack.IsWeapon || stack.IsClothing)
            {
                var equipBtn = new Button
                {
                    Text = stack.Equipped ? "unequip" : "equip",
                    CustomMinimumSize = new Vector2(60f, 0f),
                };
                var idx = i;
                var equipped = stack.Equipped;
                equipBtn.Pressed += () => OnEquipToggle(colonistId, idx, equipped);
                row.AddChild(equipBtn);
            }

            var infoBtn = new Button { Text = "i", CustomMinimumSize = new Vector2(28f, 0f) };
            var stackCopy = stack;
            infoBtn.Pressed += () => ShowItemInfo(stackCopy);
            row.AddChild(infoBtn);

            var dropBtn = new Button { Text = "drop", CustomMinimumSize = new Vector2(50f, 0f) };
            var dropIdx = i;
            dropBtn.Pressed += () => OnForceDrop(colonistId, dropIdx);
            row.AddChild(dropBtn);

            _inventoryList.AddChild(row);
        }
    }

    private static ulong ComputeInvSig(IReadOnlyList<InventoryStackView> inv)
    {
        if (inv is null || inv.Count == 0) return 0UL;
        unchecked
        {
            var h = 14695981039346656037UL;
            for (var i = 0; i < inv.Count; i++)
            {
                var s = inv[i];
                h = (h ^ (uint)(s.DefId?.GetHashCode() ?? 0)) * 1099511628211UL;
                h = (h ^ (uint)s.Count) * 1099511628211UL;
                h = (h ^ (s.Equipped ? 1UL : 0UL)) * 1099511628211UL;
                h = (h ^ (s.Locked ? 2UL : 0UL)) * 1099511628211UL;
            }
            return h == 0 ? 1UL : h;
        }
    }

    private void OnForceDrop(int colonistId, int stackIndex)
    {
        _commands.Submit(new ForceDropFromInventoryCommand(colonistId, stackIndex));
    }

    private void OnEquipToggle(int colonistId, int stackIndex, bool currentlyEquipped)
    {
        if (currentlyEquipped)
            _commands.Submit(new UnequipInventoryCommand(colonistId, stackIndex));
        else
            _commands.Submit(new EquipFromInventoryCommand(colonistId, stackIndex));
    }

    private void ShowItemInfo(InventoryStackView stack)
    {
        var title = !string.IsNullOrEmpty(stack.WrappedDefId)
            ? MinifiedLabel(stack.WrappedDefId) : stack.DisplayName;
        _itemInfoDialog.Title = title;
        var lines = new System.Text.StringBuilder();
        lines.AppendLine(!string.IsNullOrEmpty(stack.WrappedDefId)
            ? MinifiedDescription(stack.WrappedDefId) : stack.Description);
        lines.AppendLine();
        lines.AppendLine($"weight: {stack.Weight:F1} kg ea");
        lines.AppendLine($"bulk: {stack.Bulk:F1} L ea");
        lines.AppendLine($"sell value: {stack.SellValue} silver ea");
        lines.AppendLine($"count: {stack.Count}");
        if (stack.Locked) lines.AppendLine("LOCKED — auto-systems will not touch this stack");
        if (stack.Equipped) lines.AppendLine("EQUIPPED");
        _itemInfoDialog.DialogText = lines.ToString();
        _itemInfoDialog.PopupCentered();
    }

    private void ShowTree(SimSnapshot snap, int id)
    {
        _emptyLabel.Visible = false;
        _colonistBox.Visible = false;
        _itemBox.Visible = false;
        _blueprintBox.Visible = false;
        _structureBox.Visible = false;
        _treeBox.Visible = true;
        for (var i = 0; i < snap.Trees.Count; i++)
        {
            var t = snap.Trees[i];
            if (t.EntityId != id) continue;
            _treeHeader.Text =
                $"pine #{id}\n" +
                $"tile ({t.TileX}, {t.TileY})\n" +
                $"growth {t.Growth:F0}%";
            _treeHealthBar.MaxValue = 30;
            _treeHealthBar.Value = t.Health;
            var designated = HasChopDesignation(snap, t.TileX, t.TileY);
            _designateChopBtn.Disabled = designated;
            _cancelChopBtn.Disabled = !designated;
            return;
        }
        _treeHeader.Text = $"pine #{id} (felled)";
        _designateChopBtn.Disabled = true;
        _cancelChopBtn.Disabled = true;
    }

    private void ShowItem(SimSnapshot snap, int id)
    {
        _emptyLabel.Visible = false;
        _colonistBox.Visible = false;
        _treeBox.Visible = false;
        _blueprintBox.Visible = false;
        _structureBox.Visible = false;
        _itemBox.Visible = true;
        for (var i = 0; i < snap.Items.Count; i++)
        {
            var it = snap.Items[i];
            if (it.EntityId != id) continue;
            var label = it.Kind == ItemKind.Minified
                ? MinifiedLabel(it.MinifiedDefId)
                : KindLabel(it.Kind);
            _itemHeader.Text =
                $"{label} ×{it.Count}\n" +
                $"tile ({it.TileX}, {it.TileY})";
            _itemDescription.Text = it.Kind == ItemKind.Minified
                ? MinifiedDescription(it.MinifiedDefId)
                : KindDescription(it.Kind);
            _forbidSyncing = true;
            _forbidCheck.ButtonPressed = it.Forbidden;
            _forbidSyncing = false;
            return;
        }
        _itemHeader.Text = $"item #{id} (gone)";
        _itemDescription.Text = "stack picked up or merged.";
        _forbidSyncing = true;
        _forbidCheck.ButtonPressed = false;
        _forbidSyncing = false;
    }

    private void OnForbidToggled(bool pressed)
    {
        if (_forbidSyncing) return;
        if (_selection.SelectedItemId is not int id) return;
        _commands.Submit(new SetItemForbiddenCommand(id, pressed));
    }

    private static string KindLabel(ItemKind kind) => kind switch
    {
        ItemKind.Wood => "wood",
        ItemKind.Wheat => "wheat",
        ItemKind.Minified => "minified thing",
        _ => "item",
    };

    private static string KindDescription(ItemKind kind) => kind switch
    {
        ItemKind.Wood => "rough cut from a felled pine. fuel, walls, and tool handles. stacks to 50.",
        ItemKind.Wheat => "harvested grain. food crop yield. stacks to 50.",
        ItemKind.Minified => "a packaged structure ready to reinstall.",
        _ => "raw resource.",
    };

    // Resolve a minified item's wrapped blueprint to its display name —
    // ground stacks come with the wrapper id; if none provided, fall back
    // to the generic minified label.
    private static string MinifiedLabel(string? wrappedDefId)
    {
        if (string.IsNullOrEmpty(wrappedDefId)) return "minified thing";
        if (BlueprintCatalog.TryGet(wrappedDefId, out var def) && def is not null)
            return $"minified {def.DisplayName}";
        return $"minified {wrappedDefId}";
    }

    private static string MinifiedDescription(string? wrappedDefId)
    {
        if (string.IsNullOrEmpty(wrappedDefId)) return "a packaged structure ready to reinstall.";
        if (BlueprintCatalog.TryGet(wrappedDefId, out var def) && def is not null)
            return $"a packaged {def.DisplayName} ready to reinstall.";
        return "a packaged structure ready to reinstall.";
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

    private void OnDesignateChop()
    {
        if (_selection.SelectedTreeId is not int id) return;
        var snap = _publisher.Current;
        for (var i = 0; i < snap.Trees.Count; i++)
        {
            var t = snap.Trees[i];
            if (t.EntityId != id) continue;
            if (HasChopDesignation(snap, t.TileX, t.TileY)) return;
            _commands.Submit(new StampDesignationsCommand(
                DesignationKind.ChopTree,
                new TileRect(t.TileX, t.TileY, t.TileX, t.TileY)));
            return;
        }
    }

    private void OnCancelChop()
    {
        if (_selection.SelectedTreeId is not int id) return;
        var snap = _publisher.Current;
        for (var i = 0; i < snap.Trees.Count; i++)
        {
            var t = snap.Trees[i];
            if (t.EntityId != id) continue;
            _commands.Submit(new EraseInRectCommand(
                new TileRect(t.TileX, t.TileY, t.TileX, t.TileY)));
            return;
        }
    }

    private static string KindName(NeedKind kind) => kind switch
    {
        NeedKind.Hunger => "hunger",
        NeedKind.Thirst => "thirst",
        NeedKind.Energy => "energy",
        _ => "?",
    };

    private static string WorkLabel(WorkKind kind) => kind switch
    {
        WorkKind.ChopTree => "chopping",
        WorkKind.HaulItem => "hauling",
        WorkKind.CutPlant => "cutting",
        WorkKind.HarvestPlant => "harvesting",
        WorkKind.Sow => "sowing",
        _ => "working",
    };
}
