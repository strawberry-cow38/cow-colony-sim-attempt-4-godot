using CowColonySim.Sim;
using Xunit;

namespace CowColonySim.Tests;

public class SimRuntimeSmokeTests
{
    [Fact]
    public void Start_then_stop_advances_snapshot_tick()
    {
        using var runtime = new SimRuntime();
        runtime.Start();
        var deadline = DateTime.UtcNow.AddSeconds(1);
        while (runtime.LatestSnapshot.TickNumber < 5 && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(10);
        }
        runtime.Stop();
        Assert.True(runtime.LatestSnapshot.TickNumber >= 5,
            $"expected >=5 ticks within 1s, got {runtime.LatestSnapshot.TickNumber}");
    }
}
