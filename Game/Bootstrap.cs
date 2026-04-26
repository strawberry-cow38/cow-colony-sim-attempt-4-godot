using CowColonySim.Game.Camera;
using CowColonySim.Game.Colonists;
using CowColonySim.Game.Debug;
using CowColonySim.Game.Render;
using CowColonySim.Game.Selection;
using CowColonySim.Game.Terrain;
using CowColonySim.Game.Time;
using CowColonySim.Game.UI;
using CowColonySim.Sim;
using CowColonySim.Sim.Logging;
using CowColonySim.Sim.Pathfinding;
using CowColonySim.Sim.Systems;
using CowColonySim.Sim.Terrain;
using CowColonySim.Sim.World.Components;
using Godot;

namespace CowColonySim.Game;

public partial class Bootstrap : Node3D
{
    private const int PreviewTileCount = 256;

    private SimRuntime? _runtime;
    private Heightfield? _heightfield;
    private HeightfieldGenerator.Settings _genSettings;

    public override void _Ready()
    {
        SimLog.Configure();
        _runtime = new SimRuntime();

        _heightfield = new Heightfield(PreviewTileCount, PreviewTileCount);
        _genSettings = new HeightfieldGenerator.Settings();
        HeightfieldGenerator.Generate(_heightfield, _genSettings);

        var grid = new HeightGrid(_heightfield);
        var planner = new PathPlanner(grid);
        _runtime.Scheduler.Register(new CommandSystem(
            _runtime.Commands, _runtime.World, planner, grid));
        _runtime.Scheduler.Register(new NeedDecaySystem(_runtime.World));
        _runtime.Scheduler.Register(new JobSystem(_runtime.World, planner, grid));
        _runtime.Scheduler.Register(new WanderSystem(_runtime.World, planner, grid));
        SpawnColonists(_runtime);
        SpawnNeedSpots(_runtime);
        _runtime.Start();

        AddEnvironment();
        AddSun();
        AddCameraRig();
        AddTerrain(_heightfield);
        AddBackground(_genSettings);
        AddVertexOverlay(_heightfield);
        AddColonists(_runtime, _heightfield);
        AddSpots(_runtime, _heightfield);
        AddPathOverlay(_runtime, _heightfield);
        var selection = AddSelectionService(_runtime, _heightfield);
        AddSelectionRing(selection, _runtime, _heightfield);
        AddInfoPanel(selection, _runtime);
        AddPerfHud(_runtime);
        AddTimeHud(_runtime);

        SimLog.Logger.Information(
            "Bootstrap ready. SimThread at {Hz} Hz. World has {Count} entities. " +
            "Heightfield {VW}x{VH} verts (rev {Rev}).",
            SimConstants.TickRateHz, _runtime.World.EntityCount,
            _heightfield.VertWidth, _heightfield.VertHeight, _heightfield.Version);
    }

    private static void SpawnColonists(SimRuntime runtime)
    {
        var center = PreviewTileCount / 2;
        runtime.World.SpawnColonist(0xCAFEBABE, center - 2, center - 2);
        runtime.World.SpawnColonist(0xDEADC0DE, center,     center);
        runtime.World.SpawnColonist(0xFACEFEED, center + 2, center + 2);
    }

    private static void SpawnNeedSpots(SimRuntime runtime)
    {
        var center = PreviewTileCount / 2;
        runtime.World.SpawnNeedSpot(NeedKind.Hunger, center - 6, center);
        runtime.World.SpawnNeedSpot(NeedKind.Thirst, center + 6, center);
        runtime.World.SpawnNeedSpot(NeedKind.Energy, center, center + 6);
    }

    // Sky-driven ambient so faceted-terrain backsides aren't pitch-black.
    // Faceted geometry is locked (4 unshared corners per tile) — when a tile
    // has 1 low + 3 high corners the flat normal points away from the sun
    // and gets nothing without an ambient term. Don't fix that by welding
    // verts. Fix it by giving the sky a real contribution.
    private void AddEnvironment()
    {
        var sky = new Sky { SkyMaterial = new ProceduralSkyMaterial() };
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = sky,
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.85f, 0.9f, 1.0f),
            AmbientLightSkyContribution = 0.0f,
            AmbientLightEnergy = 2.0f,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,
            TonemapMode = Godot.Environment.ToneMapper.Linear,
            TonemapExposure = 1.5f,
        };
        var node = new WorldEnvironment { Name = "World", Environment = env };
        AddChild(node);
    }

    private void AddSun()
    {
        var sun = new DirectionalLight3D
        {
            Name = "Sun",
            ShadowEnabled = true,
            LightEnergy = 6.0f,
            ShadowBias = 0.5f,
            ShadowNormalBias = 2.5f,
        };
        sun.Rotation = new Vector3(Mathf.DegToRad(-55f), Mathf.DegToRad(35f), 0f);
        AddChild(sun);
    }

    private void AddCameraRig()
    {
        var span = PreviewTileCount * SimConstants.GodotUnitsPerTile;
        var rig = new CameraRig { Name = "CameraRig" };
        rig.Configure(boundsMax: new Vector2(span, span),
                      startCenter: new Vector2(span * 0.5f, span * 0.5f),
                      maxDistance: span * 0.6f);
        AddChild(rig);
    }

    private void AddBackground(HeightfieldGenerator.Settings settings)
    {
        var bg = new LODBackground { Name = "LODBackground" };
        AddChild(bg);
        bg.Build(PreviewTileCount, settings);
    }

    private void AddTerrain(Heightfield field)
    {
        var terrain = new TerrainRenderer { Name = "Terrain" };
        AddChild(terrain);
        terrain.Build(field);
    }

    private void AddVertexOverlay(Heightfield field)
    {
        var overlay = new TerrainVertexOverlay { Name = "VertexOverlay" };
        AddChild(overlay);
        overlay.Build(field);
    }

    private void AddPerfHud(SimRuntime runtime)
    {
        var hud = new PerfHud { Name = "PerfHud" };
        hud.Configure(runtime);
        AddChild(hud);
    }

    private void AddTimeHud(SimRuntime runtime)
    {
        var hud = new TimeHud { Name = "TimeHud" };
        hud.Configure(runtime);
        AddChild(hud);
    }

    private void AddColonists(SimRuntime runtime, Heightfield field)
    {
        var renderer = new ColonistsRenderer { Name = "Colonists" };
        renderer.Configure(runtime.Publisher, field);
        AddChild(renderer);
    }

    private void AddSpots(SimRuntime runtime, Heightfield field)
    {
        var renderer = new SpotsRenderer { Name = "Spots" };
        renderer.Configure(runtime.Publisher, field);
        AddChild(renderer);
    }

    private void AddPathOverlay(SimRuntime runtime, Heightfield field)
    {
        var overlay = new PathOverlay { Name = "PathOverlay" };
        overlay.Configure(runtime.Publisher, field);
        AddChild(overlay);
    }

    private SelectionService AddSelectionService(SimRuntime runtime, Heightfield field)
    {
        var sel = new SelectionService { Name = "Selection" };
        sel.Configure(runtime.Publisher, runtime.Commands, field);
        AddChild(sel);
        return sel;
    }

    private void AddSelectionRing(SelectionService selection, SimRuntime runtime, Heightfield field)
    {
        var ring = new SelectionRing { Name = "SelectionRing" };
        ring.Configure(selection, runtime.Publisher, field);
        AddChild(ring);
    }

    private void AddInfoPanel(SelectionService selection, SimRuntime runtime)
    {
        var panel = new InfoPanel { Name = "InfoPanel" };
        panel.Configure(selection, runtime.Publisher);
        AddChild(panel);
    }

    public override void _ExitTree()
    {
        _runtime?.Dispose();
        _runtime = null;
        SimLog.Reset();
    }
}
