using CowColonySim.Sim;
using CowColonySim.Sim.Logging;
using Godot;

namespace CowColonySim.Game;

public partial class Bootstrap : Node
{
    private SimRuntime? _runtime;

    public override void _Ready()
    {
        SimLog.Configure();
        _runtime = new SimRuntime();
        _runtime.Start();
        SimLog.Logger.Information("Sim runtime started at {Hz} Hz", SimConstants.TickRateHz);
    }

    public override void _ExitTree()
    {
        _runtime?.Dispose();
        _runtime = null;
    }
}
