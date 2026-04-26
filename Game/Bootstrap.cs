using CowColonySim.Game.Terrain;
using CowColonySim.Sim;
using CowColonySim.Sim.Logging;
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
        _runtime.Start();

        _heightfield = new Heightfield(PreviewTileCount, PreviewTileCount);
        HeightfieldGenerator.Generate(_heightfield, new HeightfieldGenerator.Settings());

        AddSun();
        AddCamera();
        AddTerrain(_heightfield);

        SimLog.Logger.Information(
            "Bootstrap ready. SimThread at {Hz} Hz. World has {Count} entities. " +
            "Heightfield {VW}x{VH} verts (rev {Rev}).",
            SimConstants.TickRateHz, _runtime.World.EntityCount,
            _heightfield.VertWidth, _heightfield.VertHeight, _heightfield.Version);
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

    private void AddCamera()
    {
        var halfSpan = (PreviewTileCount * SimConstants.GodotUnitsPerTile) * 0.5f;
        var camera = new Camera3D
        {
            Name = "Camera",
            Position = new Vector3(halfSpan, halfSpan * 1.5f, halfSpan * 2f),
            Far = 20_000f,
        };
        camera.LookAt(new Vector3(halfSpan, 0f, halfSpan), Vector3.Up);
        AddChild(camera);
    }

    private void AddTerrain(Heightfield field)
    {
        var terrain = new TerrainRenderer { Name = "Terrain" };
        AddChild(terrain);
        terrain.Build(field);
    }

    public override void _ExitTree()
    {
        _runtime?.Dispose();
        _runtime = null;
        SimLog.Reset();
    }
}
