using CowColonySim.Sim;
using CowColonySim.Sim.Systems;
using Xunit;

namespace CowColonySim.Tests;

public class SimRuntimeSmokeTests
{
    [Fact]
    public void Ticks_advance_when_started()
    {
        using var runtime = new SimRuntime();
        var counter = new Counter();
        runtime.Scheduler.Register(counter);

        runtime.Start();

        // Wait for at least a handful of ticks at 60 Hz. Generous timeout to
        // tolerate slow CI; we only assert that the loop is alive.
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (Volatile.Read(ref counter.Count) < 5 && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(10);
        }

        Assert.True(counter.Count >= 5, $"Expected >= 5 ticks, got {counter.Count}");
    }

    [Fact]
    public void Cannot_start_twice()
    {
        using var runtime = new SimRuntime();
        runtime.Start();
        Assert.Throws<InvalidOperationException>(() => runtime.Start());
    }

    [Fact]
    public void Snapshot_publishes_each_tick()
    {
        using var runtime = new SimRuntime();
        runtime.Start();

        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (runtime.Publisher.Current.TickNumber < 5 && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(10);
        }

        var snap = runtime.Publisher.Current;
        Assert.True(snap.TickNumber >= 5,
            $"Expected snapshot tick >= 5, got {snap.TickNumber}");
        Assert.True(snap.ElapsedSeconds > 0);
    }

    private sealed class Counter : ITickSystem
    {
        public int Count;
        public void Tick(TickContext ctx) => Interlocked.Increment(ref Count);
    }
}
