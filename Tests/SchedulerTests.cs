using CowColonySim.Sim.Systems;
using Xunit;

namespace CowColonySim.Tests;

public class SchedulerTests
{
    [Fact]
    public void Runs_systems_in_registration_order()
    {
        var scheduler = new Scheduler();
        var log = new List<int>();

        scheduler.Register(new RecordingSystem(log, 1));
        scheduler.Register(new RecordingSystem(log, 2));
        scheduler.Register(new RecordingSystem(log, 3));

        scheduler.Tick(new TickContext(0, 0.0));

        Assert.Equal(new[] { 1, 2, 3 }, log);
    }

    [Fact]
    public void Forwards_tick_context_to_systems()
    {
        var scheduler = new Scheduler();
        TickContext? captured = null;
        scheduler.Register(new LambdaSystem(ctx => captured = ctx));

        scheduler.Tick(new TickContext(42, 1.0 / 60.0));

        Assert.NotNull(captured);
        Assert.Equal(42, captured!.Value.TickNumber);
        Assert.Equal(1.0 / 60.0, captured.Value.FixedDeltaSeconds);
    }

    private sealed class RecordingSystem : ITickSystem
    {
        private readonly List<int> _log;
        private readonly int _id;

        public RecordingSystem(List<int> log, int id)
        {
            _log = log;
            _id = id;
        }

        public void Tick(TickContext ctx) => _log.Add(_id);
    }

    private sealed class LambdaSystem : ITickSystem
    {
        private readonly Action<TickContext> _action;

        public LambdaSystem(Action<TickContext> action)
        {
            _action = action;
        }

        public void Tick(TickContext ctx) => _action(ctx);
    }
}
