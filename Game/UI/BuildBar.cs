using CowColonySim.Sim.Blueprints;
using Godot;

namespace CowColonySim.Game.UI;

// Bottom-center "Build" tab. Click to open a category palette above it;
// click a category to swap the tool palette to that category's blueprints
// and designators. Clicking a tool sets BuildToolService.ActiveToolId; other
// systems read that and own the actual placement / edit behaviour.
//
// Categories and tools are hardcoded here for now (placeholder phase) —
// will be data-driven once we have real blueprints to register.
public partial class BuildBar : CanvasLayer
{
    private BuildToolService _tools = null!;

    private Button _buildButton = null!;
    private PanelContainer _popup = null!;
    private VBoxContainer _categoryList = null!;
    private VBoxContainer _toolList = null!;
    private Label _toolHeader = null!;
    private Label _layerLabel = null!;

    private readonly List<Category> _categories = BuildCategories();

    private static List<Category> BuildCategories()
    {
        var cats = new List<Category>
        {
            new Category("debug_terrain", "Debug Terrain", new[]
            {
                new Tool("debug_terrain.raise_vertex",  "Raise Vertex (+0.75m)"),
                new Tool("debug_terrain.lower_vertex",  "Lower Vertex (-0.75m)"),
                new Tool("debug_terrain.flatten_rect",  "Flatten Rect"),
            }),
            new Category("edit", "Edit", new[]
            {
                new Tool("edit.erase", "Erase (drag rect)"),
            }),
            new Category("zones", "Zones", new[]
            {
                new Tool("zone.stockpile", "Stockpile (drag rect)"),
                new Tool("zone.farm",      "Farm (drag rect)"),
                new Tool("zone.delete",    "Delete Zone (drag rect)"),
            }),
            new Category("designators", "Designators", new[]
            {
                new Tool("designate.chop_tree", "Chop Trees (drag rect)"),
                new Tool("designate.mine",      "Mine (drag rect)"),
                new Tool("designate.harvest",   "Harvest (drag rect)"),
                new Tool("designate.cut_plant", "Cut Plants (drag rect)"),
            }),
        };

        var blueprintTools = new List<Tool>();
        foreach (var def in BlueprintCatalog.All.Values)
        {
            var modeHint = def.Placement switch
            {
                PlacementMode.Single => "click",
                PlacementMode.LineDrag => "line drag",
                PlacementMode.SpacedDrag => "spaced drag",
                PlacementMode.Footprint => def.Rotatable ? "click, R rotate" : "click",
                _ => "",
            };
            blueprintTools.Add(new Tool($"blueprint.{def.Id}", $"{def.DisplayName} ({modeHint})"));
        }
        cats.Add(new Category("blueprints", "Blueprints", blueprintTools));
        return cats;
    }

    private string _activeCategoryId = string.Empty;
    private readonly Dictionary<string, Button> _toolButtons = new();
    private readonly Dictionary<string, Button> _categoryButtons = new();

    public void Configure(BuildToolService tools) => _tools = tools;

    public override void _Ready()
    {
        Layer = 90;

        _buildButton = new Button
        {
            Text = "Build",
            ToggleMode = true,
            CustomMinimumSize = new Vector2(96f, 32f),
            AnchorLeft = 0.5f,
            AnchorRight = 0.5f,
            AnchorTop = 1f,
            AnchorBottom = 1f,
            OffsetLeft = -48f,
            OffsetRight = 48f,
            OffsetTop = -40f,
            OffsetBottom = -8f,
        };
        _buildButton.Toggled += OnBuildToggled;
        AddChild(_buildButton);

        _layerLabel = new Label
        {
            Text = "stack +0  (Q/E)",
            AnchorLeft = 0.5f, AnchorRight = 0.5f,
            AnchorTop = 1f, AnchorBottom = 1f,
            OffsetLeft = 56f, OffsetRight = 240f,
            OffsetTop = -36f, OffsetBottom = -12f,
            Visible = false,
        };
        _layerLabel.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f));
        _layerLabel.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f));
        _layerLabel.AddThemeConstantOverride("outline_size", 4);
        AddChild(_layerLabel);
        _tools.BuildLayerChanged += OnBuildLayerChanged;
        _tools.ToolChanged += OnToolChangedForLayer;

        _popup = new PanelContainer
        {
            AnchorLeft = 0.5f,
            AnchorRight = 0.5f,
            AnchorTop = 1f,
            AnchorBottom = 1f,
            OffsetLeft = -240f,
            OffsetRight = 240f,
            OffsetTop = -260f,
            OffsetBottom = -48f,
            Visible = false,
        };
        AddChild(_popup);

        var row = new HBoxContainer { CustomMinimumSize = new Vector2(480f, 0f) };
        _popup.AddChild(row);

        var leftPanel = new PanelContainer { CustomMinimumSize = new Vector2(160f, 0f) };
        row.AddChild(leftPanel);
        _categoryList = new VBoxContainer();
        leftPanel.AddChild(_categoryList);

        var rightPanel = new PanelContainer();
        rightPanel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(rightPanel);
        var rightBox = new VBoxContainer();
        rightPanel.AddChild(rightBox);
        _toolHeader = new Label { Text = "select a category" };
        rightBox.AddChild(_toolHeader);
        _toolList = new VBoxContainer();
        rightBox.AddChild(_toolList);

        BuildCategoryButtons();
    }

    private void BuildCategoryButtons()
    {
        foreach (var cat in _categories)
        {
            var btn = new Button
            {
                Text = cat.Name,
                ToggleMode = true,
                CustomMinimumSize = new Vector2(0f, 28f),
            };
            btn.Pressed += () => SelectCategory(cat.Id);
            _categoryList.AddChild(btn);
            _categoryButtons[cat.Id] = btn;
        }
    }

    private void OnBuildLayerChanged(int layer)
    {
        _layerLabel.Text = $"stack +{layer}  (Q/E)";
    }

    private void OnToolChangedForLayer(string toolId)
    {
        _layerLabel.Visible = toolId.StartsWith("blueprint.");
    }

    private void OnBuildToggled(bool open)
    {
        _popup.Visible = open;
        if (!open && !string.IsNullOrEmpty(_tools.ActiveToolId))
        {
            _tools.Clear();
            ClearToolHighlight();
        }
    }

    private void SelectCategory(string id)
    {
        _activeCategoryId = id;
        foreach (var (cid, btn) in _categoryButtons)
        {
            btn.ButtonPressed = cid == id;
        }
        RefreshToolList();
    }

    private void RefreshToolList()
    {
        foreach (var child in _toolList.GetChildren()) child.QueueFree();
        _toolButtons.Clear();

        var cat = _categories.Find(c => c.Id == _activeCategoryId);
        if (cat is null)
        {
            _toolHeader.Text = "select a category";
            return;
        }

        _toolHeader.Text = cat.Name;
        foreach (var t in cat.Tools)
        {
            var btn = new Button
            {
                Text = t.Name,
                ToggleMode = true,
                CustomMinimumSize = new Vector2(0f, 28f),
            };
            var toolId = t.Id;
            btn.Pressed += () => SelectTool(toolId);
            _toolList.AddChild(btn);
            _toolButtons[t.Id] = btn;
        }
    }

    private void SelectTool(string toolId)
    {
        var alreadyActive = _tools.ActiveToolId == toolId;
        if (alreadyActive)
        {
            _tools.Clear();
            ClearToolHighlight();
            return;
        }
        _tools.SetActive(toolId);
        foreach (var (id, btn) in _toolButtons) btn.ButtonPressed = id == toolId;
    }

    private void ClearToolHighlight()
    {
        foreach (var (_, btn) in _toolButtons) btn.ButtonPressed = false;
    }

    public override void _UnhandledInput(InputEvent ev)
    {
        if (ev is InputEventKey k && k.Pressed && !k.Echo && k.PhysicalKeycode == Key.Escape)
        {
            if (!string.IsNullOrEmpty(_tools.ActiveToolId))
            {
                _tools.Clear();
                ClearToolHighlight();
                GetViewport().SetInputAsHandled();
            }
            else if (_buildButton.ButtonPressed)
            {
                _buildButton.ButtonPressed = false;
                GetViewport().SetInputAsHandled();
            }
        }
    }

    private sealed record Tool(string Id, string Name);
    private sealed record Category(string Id, string Name, IReadOnlyList<Tool> Tools);
}
