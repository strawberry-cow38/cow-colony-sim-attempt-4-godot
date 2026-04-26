using System.Diagnostics;

namespace CowColonySim.Sim.Systems;

public sealed class Scheduler
{
    private readonly List<ITickSystem> _systems = new();

    public IReadOnlyList<ITickSystem> Systems => _systems;
    public PerfMetrics Metrics { get; } = new();

    public void Register(ITickSystem system)
    {
        _systems.Add(system);
    }

    public void Tick(TickContext ctx)
    {
        var totalStart = Stopwatch.GetTimestamp();
        for (var i = 0; i < _systems.Count; i++)
        {
            var sysStart = Stopwatch.GetTimestamp();
            _systems[i].Tick(ctx);
            Metrics.RecordSystem(
                _systems[i].GetType().Name,
                Stopwatch.GetTimestamp() - sysStart);
        }
        Metrics.RecordTickTotal(Stopwatch.GetTimestamp() - totalStart);
    }
}
