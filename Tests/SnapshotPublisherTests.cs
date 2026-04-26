using CowColonySim.Sim.Snapshots;
using Xunit;

namespace CowColonySim.Tests;

public class SnapshotPublisherTests
{
    [Fact]
    public void Defaults_to_empty()
    {
        var pub = new SnapshotPublisher();
        Assert.Same(SimSnapshot.Empty, pub.Current);
    }

    [Fact]
    public void Publish_replaces_current()
    {
        var pub = new SnapshotPublisher();
        var snap = new SimSnapshot(5, 5.0 / 60.0, 0, Array.Empty<ColonistView>(), Array.Empty<SpotView>(), Array.Empty<PathView>());
        pub.Publish(snap);
        Assert.Same(snap, pub.Current);
    }

    [Fact]
    public async Task Latest_publish_wins_under_concurrent_writes()
    {
        var pub = new SnapshotPublisher();
        var done = new ManualResetEventSlim(false);

        var writer = Task.Run(() =>
        {
            for (var i = 1; i <= 10_000; i++)
            {
                pub.Publish(new SimSnapshot(i, i / 60.0, 0, Array.Empty<ColonistView>(), Array.Empty<SpotView>(), Array.Empty<PathView>()));
            }
            done.Set();
        });

        long lastSeen = 0;
        while (!done.IsSet)
        {
            var t = pub.Current.TickNumber;
            Assert.True(t >= lastSeen, $"Snapshot tick went backwards: {lastSeen} -> {t}");
            lastSeen = t;
        }
        await writer;
        Assert.Equal(10_000, pub.Current.TickNumber);
    }
}
