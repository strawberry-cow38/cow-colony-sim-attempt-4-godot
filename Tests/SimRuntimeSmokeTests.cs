using CowColonySim.Sim;
using CowColonySim.Sim.Time;
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

    [Fact]
    public void Pause_freezes_snapshot_tick()
    {
        using var runtime = new SimRuntime();
        runtime.Speed.Set(SimSpeed.Paused);
        runtime.Start();
        Thread.Sleep(150);
        var snapAtPause = runtime.LatestSnapshot.TickNumber;
        Thread.Sleep(150);
        var snapStillPaused = runtime.LatestSnapshot.TickNumber;
        runtime.Stop();
        Assert.Equal(snapAtPause, snapStillPaused);
        Assert.Equal(0, snapStillPaused);
    }

    [Fact]
    public void Snapshot_carries_speed_and_game_time()
    {
        using var runtime = new SimRuntime();
        runtime.Speed.Set(SimSpeed.Fast);
        runtime.Start();
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (runtime.LatestSnapshot.TickNumber < 60 && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(20);
        }
        runtime.Stop();
        var snap = runtime.LatestSnapshot;
        Assert.Equal(SimSpeed.Fast, snap.Speed);
        Assert.True(snap.GameTime > CalendarConstants.Epoch);
    }
}
