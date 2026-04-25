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
        AddMoon();
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
        var skyMat = new PhysicalSkyMaterial
        {
            SunDiskScale = 4f,
            UseDebanding = true,
        };
        var sky = new Sky { SkyMaterial = skyMat };
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = sky,
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightSkyContribution = 0.7f,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            TonemapExposure = 1.0f,
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
            LightEnergy = 1.0f,
            SkyMode = DirectionalLight3D.SkyModeEnum.LightAndSky,
        };
        sun.Rotation = new Vector3(Mathf.DegToRad(-55f), Mathf.DegToRad(35f), 0f);
        AddChild(sun);
    }

    private void AddMoon()
    {
        var moon = new DirectionalLight3D
        {
            Name = "Moon",
            ShadowEnabled = false,
            LightEnergy = 0.15f,
            LightColor = new Color(0.7f, 0.8f, 1.0f),
            SkyMode = DirectionalLight3D.SkyModeEnum.LightOnly,
        };
        moon.Rotation = new Vector3(Mathf.DegToRad(-30f), Mathf.DegToRad(35f + 180f), 0f);
        AddChild(moon);
    }

    private void AddCamera()
    {
        var halfMap = (256 * SimConstants.GodotUnitsPerTile) * 0.5f;
        var rig = new GimbalCamera { Name = "CameraRig" };
        AddChild(rig);
        rig.SetFocus(new Vector3(halfMap, 0f, halfMap));
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
