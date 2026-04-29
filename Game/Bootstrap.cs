using CowColonySim.Game.Audio;
using CowColonySim.Game.Camera;
using CowColonySim.Game.Colonists;
using CowColonySim.Game.Debug;
using CowColonySim.Game.Render;
using CowColonySim.Game.Selection;
using CowColonySim.Game.Terrain;
using CowColonySim.Game.Time;
using CowColonySim.Game.UI;
using CowColonySim.Sim;
using CowColonySim.Sim.Designations;
using CowColonySim.Sim.Logging;
using CowColonySim.Sim.Pathfinding;
using CowColonySim.Sim.Systems;
using CowColonySim.Sim.Terrain;
using CowColonySim.Sim.Weather;
using CowColonySim.Sim.World.Components;
using CowColonySim.Sim.Zones;
using Godot;

namespace CowColonySim.Game;

public partial class Bootstrap : Node3D
{
    private const int PreviewTileCount = 256;

    private SimRuntime? _runtime;
    private Heightfield? _heightfield;
    private HeightfieldGenerator.Settings _genSettings;
    private ChunkedTerrainRenderer? _terrain;

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
        _runtime.Scheduler.Register(new UnstickSystem(_runtime.World, grid));
        _runtime.Scheduler.Register(new JobSystem(_runtime.World, planner, grid));
        _runtime.Scheduler.Register(new ChopJobSystem(_runtime.World, planner, grid));
        _runtime.Scheduler.Register(new MineJobSystem(_runtime.World, planner, grid));
        _runtime.Scheduler.Register(new PlantJobSystem(_runtime.World, planner, grid));
        _runtime.Scheduler.Register(new FarmAutoDesignateSystem(_runtime.World));
        _runtime.Scheduler.Register(new SowJobSystem(_runtime.World, planner, grid));
        _runtime.Scheduler.Register(new ForcePickupSystem(_runtime.World, planner, grid));
        // Construction runs before haul: a wood-hungry blueprint should
        // claim an idle colonist before generic stockpile-haul would
        // ship the same wood somewhere else.
        _runtime.Scheduler.Register(new ConstructionJobSystem(_runtime.World, planner, grid));
        _runtime.Scheduler.Register(new HaulSystem(_runtime.World, planner, grid));
        _runtime.Scheduler.Register(new StructureWorkSystem(_runtime.World, planner, grid));
        _runtime.Scheduler.Register(new WanderSystem(_runtime.World, planner, grid));
        var lighting = new LightingSystem(_runtime.World, grid.Width, grid.Height);
        _runtime.Scheduler.Register(lighting);
        _runtime.Lighting = lighting;
        var weather = new WeatherSystem(_runtime.World, grid.Width, grid.Height, MapClimate.Temperate);
        _runtime.Scheduler.Register(weather);
        _runtime.Weather = weather;
        _runtime.Scheduler.Register(new PlantGrowthSystem(_runtime.World, lighting, weather));
        SpawnColonists(_runtime);
        SpawnNeedSpots(_runtime);
        SpawnDummyFrameworkObjects(_runtime);
        SpawnTrees(_runtime, _heightfield, grid);
        SpawnBoulders(_runtime, _heightfield, grid);
        _runtime.Start();

        var env = AddEnvironment();
        var sun = AddSun();
        AddDayNightCycle(_runtime, sun, env);
        AddCameraRig();
        AddTerrain(_heightfield);
        AddBorderWall();
        AddBackground(_genSettings);
        AddVertexOverlay(_heightfield);
        AddColonists(_runtime, _heightfield);
        AddSpots(_runtime, _heightfield);
        AddZones(_runtime, _heightfield);
        AddDesignations(_runtime, _heightfield);
        AddBlueprintGhosts(_runtime, _heightfield);
        AddStructures(_runtime, _heightfield);
        AddTrees(_runtime, _heightfield);
        AddBoulders(_runtime, _heightfield);
        AddChopAudio(_runtime, _heightfield);
        AddTreeFallAudio(_runtime, _heightfield);
        AddItems(_runtime, _heightfield);
        AddPathOverlay(_runtime, _heightfield);
        var selection = AddSelectionService(_runtime, _heightfield);
        AddSelectionRing(selection, _runtime, _heightfield);
        AddReservationOverlay(selection, _runtime, _heightfield);
        AddInfoPanel(selection, _runtime);
        AddPortraitBar(selection, _runtime);
        AddContextMenu(selection, _runtime);
        AddItemHoverLabel(_runtime, _heightfield);
        AddZoneSettingsPanel(selection, _runtime);
        AddPerfHud(_runtime);
        AddTimeHud(_runtime);
        AddBuildBar(selection);
        AddRain(_runtime);
        AddWeatherGimbal(_runtime);
        AddWorkPriorityPanel(_runtime);

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

    // One of each framework type so the renderers + snapshots have something
    // visible to bind to before tools/UI land.
    private static void SpawnDummyFrameworkObjects(SimRuntime runtime)
    {
        var center = PreviewTileCount / 2;

        var stockpileRect = new TileRect(center - 14, center - 4, center - 10, center);
        runtime.World.SpawnZone(
            zoneId: 1, ZoneType.Stockpile,
            stockpileRect, TileMask.Filled(stockpileRect),
            "Dummy Stockpile");
        var farmRect = new TileRect(center + 10, center - 4, center + 14, center);
        runtime.World.SpawnZone(
            zoneId: 2, ZoneType.Farm,
            farmRect, TileMask.Filled(farmRect),
            "Dummy Farm");

        runtime.World.SpawnDesignation(center - 8, center + 8, DesignationKind.ChopTree);
        runtime.World.SpawnDesignation(center - 6, center + 8, DesignationKind.Mine);
        runtime.World.SpawnDesignation(center - 4, center + 8, DesignationKind.Harvest);

        runtime.World.SpawnBlueprintGhost("structure.wall", center - 2, center - 10);
        runtime.World.SpawnBlueprintGhost("structure.door", center, center - 10);
        runtime.World.SpawnBlueprintGhost("workstation.crafting_table", center + 2, center - 10);
        runtime.World.SpawnBlueprintGhost("utility.ac_unit", center + 5, center - 10);
    }

    // Scatter pines deterministically across the map. Avoid a radius around
    // the colonist/zone cluster so the play area stays uncluttered while we
    // still get a forest backdrop for chop testing.
    private static void SpawnTrees(SimRuntime runtime, Heightfield field, HeightGrid grid)
    {
        const int target = 250;
        const int clearRadius = 16;
        var center = PreviewTileCount / 2;
        var rng = new Random(unchecked((int)0xCAFEBABEu));
        var placed = 0;
        var attempts = 0;
        while (placed < target && attempts < target * 8)
        {
            attempts++;
            var tx = rng.Next(2, field.VertWidth - 2);
            var ty = rng.Next(2, field.VertHeight - 2);
            var dx = tx - center;
            var dy = ty - center;
            if (dx * dx + dy * dy < clearRadius * clearRadius) continue;
            if (grid.IsBlocked(tx, ty)) continue;
            runtime.World.SpawnTree(tx, ty, unchecked((uint)rng.Next()));
            grid.MarkBlocked(tx, ty, true);
            placed++;
        }
    }

    private void AddTrees(SimRuntime runtime, Heightfield field)
    {
        var renderer = new TreesRenderer { Name = "Trees" };
        renderer.Configure(runtime.Publisher, field);
        AddChild(renderer);
    }

    private void AddBoulders(SimRuntime runtime, Heightfield field)
    {
        var renderer = new BouldersRenderer { Name = "Boulders" };
        renderer.Configure(runtime.Publisher, field);
        AddChild(renderer);
    }

    // Scatter boulders on open tiles, deterministic from a fixed seed.
    // Variant picks one of the 4 mesh buckets so the field reads varied.
    // Lower count than trees — boulders are landmarks, not background.
    private static void SpawnBoulders(SimRuntime runtime, Heightfield field, HeightGrid grid)
    {
        const int target = 80;
        const int clearRadius = 14;
        // 6 variants: 3 shapes (a/b/c) × {clean, mossy}. Mossy biased rarer
        // so the field reads stone-mostly with occasional moss accents.
        const int variantCount = 6;
        var center = PreviewTileCount / 2;
        var rng = new Random(unchecked((int)0xB0B0B0B0u));
        var placed = 0;
        var attempts = 0;
        while (placed < target && attempts < target * 8)
        {
            attempts++;
            var tx = rng.Next(2, field.VertWidth - 2);
            var ty = rng.Next(2, field.VertHeight - 2);
            var dx = tx - center;
            var dy = ty - center;
            if (dx * dx + dy * dy < clearRadius * clearRadius) continue;
            if (grid.IsBlocked(tx, ty)) continue;
            var seed = unchecked((uint)rng.Next());
            // 30% mossy. Shape (0..2) picked uniformly. Variant index:
            // 0..2 = clean, 3..5 = mossy.
            var shape = rng.Next(3);
            var mossy = rng.NextDouble() < 0.30;
            var variant = mossy ? shape + 3 : shape;
            _ = variantCount;
            runtime.World.SpawnBoulder(tx, ty, seed, variant);
            grid.MarkBlocked(tx, ty, true);
            placed++;
        }
    }

    private void AddChopAudio(SimRuntime runtime, Heightfield field)
    {
        var audio = new ChopAudio { Name = "ChopAudio" };
        audio.Configure(runtime.Publisher, field);
        AddChild(audio);
    }

    private void AddTreeFallAudio(SimRuntime runtime, Heightfield field)
    {
        var audio = new TreeFallAudio { Name = "TreeFallAudio" };
        audio.Configure(runtime.Publisher, field);
        AddChild(audio);
    }

    private void AddItems(SimRuntime runtime, Heightfield field)
    {
        var renderer = new ItemsRenderer { Name = "Items" };
        renderer.Configure(runtime.Publisher, field);
        AddChild(renderer);
    }

    // Sky-driven ambient so faceted-terrain backsides aren't pitch-black.
    // Faceted geometry is locked (4 unshared corners per tile) — when a tile
    // has 1 low + 3 high corners the flat normal points away from the sun
    // and gets nothing without an ambient term. Don't fix that by welding
    // verts. Fix it by giving the sky a real contribution.
    private Godot.Environment AddEnvironment()
    {
        var skyMat = new ProceduralSkyMaterial
        {
            SkyTopColor = new Color(0.18f, 0.42f, 0.82f),
            SkyHorizonColor = new Color(0.70f, 0.82f, 0.95f),
            GroundHorizonColor = new Color(0.70f, 0.78f, 0.85f),
            GroundBottomColor = new Color(0.18f, 0.22f, 0.20f),
            SunAngleMax = 12f,
            SunCurve = 0.15f,
        };
        var sky = new Sky { SkyMaterial = skyMat };
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = sky,
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.55f, 0.6f, 0.7f),
            AmbientLightSkyContribution = 0.0f,
            AmbientLightEnergy = 0.35f,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,
            TonemapMode = Godot.Environment.ToneMapper.Linear,
            TonemapExposure = 0.9f,
        };
        var node = new WorldEnvironment { Name = "World", Environment = env };
        AddChild(node);
        return env;
    }

    private DirectionalLight3D AddSun()
    {
        var sun = new DirectionalLight3D
        {
            Name = "Sun",
            ShadowEnabled = true,
            LightEnergy = 2.5f,
            ShadowBias = 0.1f,
            ShadowNormalBias = 2.0f,
            ShadowBlur = 0.0f,
            ShadowOpacity = 1.0f,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Orthogonal,
            DirectionalShadowMaxDistance = 1000f,
        };
        sun.Rotation = new Vector3(Mathf.DegToRad(-55f), Mathf.DegToRad(35f), 0f);
        AddChild(sun);
        return sun;
    }

    private void AddDayNightCycle(SimRuntime runtime, DirectionalLight3D sun, Godot.Environment env)
    {
        var sky = (ProceduralSkyMaterial)env.Sky.SkyMaterial;
        var cycle = new DayNightCycle { Name = "DayNightCycle" };
        cycle.Configure(runtime, sun, env, sky);
        AddChild(cycle);
    }

    private CameraRig? _cameraRig;

    private void AddCameraRig()
    {
        var span = PreviewTileCount * SimConstants.GodotUnitsPerTile;
        var rig = new CameraRig { Name = "CameraRig" };
        rig.Configure(boundsMax: new Vector2(span, span),
                      startCenter: new Vector2(span * 0.5f, span * 0.5f),
                      maxDistance: span * 0.6f);
        AddChild(rig);
        _cameraRig = rig;
    }

    private void AddBackground(HeightfieldGenerator.Settings settings)
    {
        var bg = new LODBackground { Name = "LODBackground" };
        AddChild(bg);
        bg.Build(PreviewTileCount, settings);
    }

    private void AddTerrain(Heightfield field)
    {
        var terrain = new ChunkedTerrainRenderer { Name = "Terrain" };
        AddChild(terrain);
        terrain.Build(field);
        _terrain = terrain;
    }

    private void AddBorderWall()
    {
        var wall = new BorderWall { Name = "BorderWall" };
        AddChild(wall);
        wall.Build(PreviewTileCount);
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
        hud.Configure(runtime, _cameraRig);
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

        var badges = new DraftedBadgesRenderer { Name = "DraftedBadges" };
        badges.Configure(runtime.Publisher, field);
        AddChild(badges);
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

    private void AddZones(SimRuntime runtime, Heightfield field)
    {
        var renderer = new ZonesRenderer { Name = "Zones" };
        renderer.Configure(runtime.Publisher, field);
        AddChild(renderer);
    }

    private void AddDesignations(SimRuntime runtime, Heightfield field)
    {
        var renderer = new DesignationsRenderer { Name = "Designations" };
        renderer.Configure(runtime.Publisher, field);
        AddChild(renderer);
    }

    private void AddBlueprintGhosts(SimRuntime runtime, Heightfield field)
    {
        var renderer = new BlueprintGhostsRenderer { Name = "BlueprintGhosts" };
        renderer.Configure(runtime.Publisher, field);
        AddChild(renderer);
    }

    private void AddStructures(SimRuntime runtime, Heightfield field)
    {
        var renderer = new StructuresRenderer { Name = "Structures" };
        renderer.Configure(runtime.Publisher, field);
        AddChild(renderer);
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

    private void AddRain(SimRuntime runtime)
    {
        var rain = new RainEffect { Name = "Rain" };
        rain.Configure(runtime.Publisher);
        AddChild(rain);
    }

    private void AddWeatherGimbal(SimRuntime runtime)
    {
        var gimbal = new WeatherGimbal { Name = "WeatherGimbal" };
        gimbal.Configure(_cameraRig!, runtime.Publisher);
        AddChild(gimbal);
    }

    private void AddWorkPriorityPanel(SimRuntime runtime)
    {
        var panel = new WorkPriorityPanel { Name = "WorkPriorityPanel" };
        panel.Configure(runtime.Publisher, runtime.Commands);
        AddChild(panel);
    }

    private void AddReservationOverlay(SelectionService selection, SimRuntime runtime, Heightfield field)
    {
        var overlay = new ReservationOverlay { Name = "ReservationOverlay" };
        overlay.Configure(selection, runtime.Publisher, field);
        AddChild(overlay);
    }

    private void AddInfoPanel(SelectionService selection, SimRuntime runtime)
    {
        var panel = new InfoPanel { Name = "InfoPanel" };
        panel.Configure(selection, runtime.Publisher, runtime.Commands);
        AddChild(panel);
    }

    private void AddPortraitBar(SelectionService selection, SimRuntime runtime)
    {
        var bar = new PortraitBar { Name = "PortraitBar" };
        bar.Configure(selection, runtime.Publisher, _cameraRig!);
        AddChild(bar);
    }

    private void AddContextMenu(SelectionService selection, SimRuntime runtime)
    {
        var menu = new ContextMenu { Name = "ContextMenu" };
        menu.Configure(selection, runtime.Publisher, runtime.Commands);
        AddChild(menu);
        selection.SetContextMenu(menu);
    }

    private void AddItemHoverLabel(SimRuntime runtime, Heightfield heightfield)
    {
        var hover = new ItemHoverLabel { Name = "ItemHoverLabel" };
        hover.Configure(runtime.Publisher, heightfield, _cameraRig);
        AddChild(hover);
    }

    private void AddZoneSettingsPanel(SelectionService selection, SimRuntime runtime)
    {
        var panel = new ZoneSettingsPanel { Name = "ZoneSettingsPanel" };
        panel.Configure(selection, runtime.Publisher, runtime.Commands);
        AddChild(panel);
    }

    private void AddBuildBar(SelectionService selection)
    {
        var tools = new BuildToolService { Name = "BuildTools" };
        AddChild(tools);
        selection.SetBuildTools(tools);

        var bar = new BuildBar { Name = "BuildBar" };
        bar.Configure(tools);
        AddChild(bar);

        var overlay = new TerrainEditOverlay { Name = "TerrainEditOverlay" };
        overlay.Configure(tools, _heightfield!);
        AddChild(overlay);

        var tool = new TerrainEditTool { Name = "TerrainEditTool" };
        tool.Configure(tools, overlay, _heightfield!, _terrain!, _runtime!.Commands);
        AddChild(tool);

        var rectOverlay = new RectDragOverlay { Name = "RectDragOverlay" };
        rectOverlay.Configure(_heightfield!);
        AddChild(rectOverlay);

        var ghostPreview = new BlueprintGhostPreview { Name = "BlueprintGhostPreview" };
        ghostPreview.Configure(_heightfield!);
        AddChild(ghostPreview);

        var placement = new PlacementTool { Name = "PlacementTool" };
        placement.Configure(tools, rectOverlay, ghostPreview, _heightfield!, _runtime!.Commands, _runtime!.Publisher);
        AddChild(placement);
    }

    public override void _ExitTree()
    {
        _runtime?.Dispose();
        _runtime = null;
        SimLog.Reset();
    }
}
