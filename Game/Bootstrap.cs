using CowColonySim.Sim;
using CowColonySim.Sim.Logging;
using Godot;

namespace CowColonySim.Game;

public partial class Bootstrap : Node3D
{
    private SimRuntime? _runtime;

    public override void _Ready()
    {
        SimLog.Configure();
        _runtime = new SimRuntime();
        _runtime.Start();
        SimLog.Logger.Information(
            "Bootstrap ready. SimThread at {Hz} Hz. World has {Count} entities.",
            SimConstants.TickRateHz, _runtime.World.EntityCount);
    }

    public override void _ExitTree()
    {
        _runtime?.Dispose();
        _runtime = null;
        SimLog.Reset();
    }
}
