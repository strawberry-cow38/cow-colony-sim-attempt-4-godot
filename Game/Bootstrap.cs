using CowColonySim.Sim;
using CowColonySim.Sim.Logging;
using CowColonySim.Sim.Map;
using Godot;

namespace CowColonySim.Game;

public partial class Bootstrap : Node
{
    private SimRuntime? _runtime;
    private MapHost? _map;

    public override void _Ready()
    {
        SimLog.Configure();
        _runtime = new SimRuntime();
        _map = new MapHost(new MapSettings(), _runtime.World.Store);
        _runtime.Climate = _map.Climate;
        _runtime.Scheduler.Register(_map.ClimateTick);
        _runtime.Scheduler.Register(_map.LightingTick);
        _runtime.Start();
        SimLog.Logger.Information(
            "Sim runtime started at {Hz} Hz, map {W}x{H}x{D} (z {MinZ}..{MaxZ})",
            SimConstants.TickRateHz,
            _map.Grid.Width, _map.Grid.Height, _map.Grid.Depth,
            _map.Grid.MinZ, _map.Grid.MaxZ);
    }

    public override void _ExitTree()
    {
        _runtime?.Dispose();
        _runtime = null;
        _map = null;
    }
}
