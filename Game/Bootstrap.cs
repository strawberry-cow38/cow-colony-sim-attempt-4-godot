using CowColonySim.Game.CameraRig;
using CowColonySim.Game.Terrain;
using CowColonySim.Sim;
using CowColonySim.Sim.Logging;
using CowColonySim.Sim.Map;
using Godot;

namespace CowColonySim.Game;

public partial class Bootstrap : Node3D
{
    private SimRuntime? _runtime;
    private MapHost? _map;

    public override void _Ready()
    {
        SimLog.Configure();
        _runtime = new SimRuntime();
        _map = new MapHost(new MapSettings(Seed: 1337), _runtime.World.Store);
        _runtime.Climate = _map.Climate;
        _runtime.Scheduler.Register(_map.ClimateTick);
        _runtime.Scheduler.Register(_map.LightingTick);

        AddSky();
        AddSun();
        AddCamera();
        AddTerrain(_map);

        _runtime.Start();
        SimLog.Logger.Information(
            "Sim runtime started at {Hz} Hz, map {W}x{H}x{D} (z {MinZ}..{MaxZ})",
            SimConstants.TickRateHz,
            _map.Grid.Width, _map.Grid.Height, _map.Grid.Depth,
            _map.Grid.MinZ, _map.Grid.MaxZ);
    }

    private void AddSky()
    {
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.45f, 0.65f, 0.85f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.6f, 0.7f, 0.9f),
            AmbientLightEnergy = 0.4f,
        };
        var we = new WorldEnvironment { Environment = env, Name = "WorldEnv" };
        AddChild(we);
    }

    private void AddSun()
    {
        var sun = new DirectionalLight3D
        {
            Name = "Sun",
            ShadowEnabled = true,
        };
        sun.Rotation = new Vector3(Mathf.DegToRad(-55f), Mathf.DegToRad(35f), 0f);
        AddChild(sun);
    }

    private void AddCamera()
    {
        var halfMap = (256 * SimConstants.GodotUnitsPerTile) * 0.5f;
        var cam = new FlyCamera
        {
            Name = "Camera",
            Fov = 60f,
            Near = 1f,
            Far = 80000f,
        };
        var camPos = new Vector3(halfMap - 4000f, 4500f, halfMap + 6500f);
        AddChild(cam);
        cam.LookAtFromPosition(camPos, new Vector3(halfMap, 0f, halfMap), Vector3.Up);
    }

    private void AddTerrain(MapHost map)
    {
        var terrain = new TerrainRenderer { Name = "Terrain" };
        AddChild(terrain);
        terrain.Build(map.Terrain);
    }

    public override void _ExitTree()
    {
        _runtime?.Dispose();
        _runtime = null;
        _map = null;
    }
}
