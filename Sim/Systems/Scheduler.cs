using System.Diagnostics;

namespace CowColonySim.Sim.Systems;

public sealed class Scheduler
{
    private readonly List<ITickSystem> _systems = new();
    private long _tick;

    public long CurrentTick => _tick;
    public IReadOnlyList<ITickSystem> Systems => _systems;

    public void Register(ITickSystem system)
    {
        ArgumentNullException.ThrowIfNull(system);
        _systems.Add(system);
    }

    public TickReport TickOnce()
    {
        var ctx = new TickContext(_tick, SimConstants.FixedDeltaSeconds);
        var sw = Stopwatch.StartNew();
        foreach (var sys in _systems)
        {
            sys.Tick(in ctx);
        }
        sw.Stop();
        _tick++;
        return new TickReport(ctx.TickNumber, sw.Elapsed);
    }
}

public readonly struct TickReport
{
    public readonly long TickNumber;
    public readonly TimeSpan Duration;

    public TickReport(long tickNumber, TimeSpan duration)
    {
        TickNumber = tickNumber;
        Duration = duration;
    }
}
