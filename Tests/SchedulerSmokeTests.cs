using CowColonySim.Sim;
using CowColonySim.Sim.Systems;
using Xunit;

namespace CowColonySim.Tests;

public class SchedulerSmokeTests
{
    private sealed class CountingSystem : ITickSystem
    {
        public string Name => "counter";
        public int Count { get; private set; }
        public void Tick(in TickContext ctx) => Count++;
    }

    [Fact]
    public void Scheduler_ticks_every_registered_system()
    {
        var scheduler = new Scheduler();
        var sys = new CountingSystem();
        scheduler.Register(sys);

        for (var i = 0; i < 1000; i++)
        {
            scheduler.TickOnce();
        }

        Assert.Equal(1000, sys.Count);
        Assert.Equal(1000, scheduler.CurrentTick);
    }

    [Fact]
    public void TickContext_carries_fixed_delta()
    {
        var scheduler = new Scheduler();
        double observed = 0.0;
        scheduler.Register(new LambdaSystem(ctx => observed = ctx.FixedDelta));
        scheduler.TickOnce();
        Assert.Equal(SimConstants.FixedDeltaSeconds, observed);
    }

    private sealed class LambdaSystem : ITickSystem
    {
        private readonly TickAction _action;
        public LambdaSystem(TickAction action) { _action = action; }
        public string Name => "lambda";
        public void Tick(in TickContext ctx) => _action(ctx);
        public delegate void TickAction(TickContext ctx);
    }
}
