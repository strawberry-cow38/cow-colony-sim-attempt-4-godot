using CowColonySim.Game.Camera;
using CowColonySim.Game.Colonists;
using CowColonySim.Game.Debug;
using CowColonySim.Game.Terrain;
using CowColonySim.Sim;
using CowColonySim.Sim.Logging;
using CowColonySim.Sim.Systems;
using CowColonySim.Sim.Terrain;
using Godot;

namespace CowColonySim.Game;

public partial class Bootstrap : Node3D
{
    private const int PreviewTileCount = 64;

    private SimRuntime? _runtime;
    private Heightfield? _heightfield;

    public override void _Ready()
    {
        SimLog.Configure();
        _runtime = new SimRuntime();

        _heightfield = new Heightfield(PreviewTileCount, PreviewTileCount);
        HeightfieldGenerator.Generate(_heightfield, new HeightfieldGenerator.Settings());

        _runtime.Scheduler.Register(new WanderSystem(
            _runtime.World, PreviewTileCount, PreviewTileCount));
        SpawnColonists(_runtime);
        _runtime.Start();

        AddSun();
        AddCameraRig();
        AddTerrain(_heightfield);
        AddVertexOverlay(_heightfield);
        AddColonists(_runtime, _heightfield);
        AddPerfHud(_runtime);

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

    private void AddSun()
    {
        var sun = new DirectionalLight3D
        {
            Name = "Sun",
            ShadowEnabled = true,
            LightEnergy = 0.9f,
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
                      startCenter: new Vector2(span * 0.5f, span * 0.5f));
        AddChild(rig);
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

    private void AddColonists(SimRuntime runtime, Heightfield field)
    {
        var renderer = new ColonistsRenderer { Name = "Colonists" };
        renderer.Configure(runtime.Publisher, field);
        AddChild(renderer);
    }

    public override void _ExitTree()
    {
        _runtime?.Dispose();
        _runtime = null;
        SimLog.Reset();
    }
}
