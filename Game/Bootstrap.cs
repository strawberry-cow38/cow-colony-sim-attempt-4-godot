using CowColonySim.Sim;
using Godot;

namespace CowColonySim.Game;

public partial class Bootstrap : Node3D
{
    private SimRuntime? _runtime;

    public override void _Ready()
    {
        _runtime = new SimRuntime();
        _runtime.Start();
        GD.Print($"Bootstrap ready. SimThread running at {SimConstants.TickRateHz} Hz.");
    }

    public override void _ExitTree()
    {
        _runtime?.Dispose();
        _runtime = null;
    }
}
