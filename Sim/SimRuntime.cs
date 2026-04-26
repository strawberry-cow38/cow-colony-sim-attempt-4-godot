using System.Diagnostics;
using CowColonySim.Sim.Commands;
using CowColonySim.Sim.Snapshots;
using CowColonySim.Sim.Systems;
using CowColonySim.Sim.Time;
using CowColonySim.Sim.World;
using CowColonySim.Sim.World.Components;

namespace CowColonySim.Sim;

// Owns the dedicated SimThread, the scheduler, the world, and the snapshot
// publisher. Each tick: scheduler runs, then SimRuntime builds an immutable
// SimSnapshot and publishes it. Game-side code reads only the snapshot.
public sealed class SimRuntime : IDisposable
{
    private readonly Scheduler _scheduler = new();
    private readonly SimWorld _world = new();
    private readonly SnapshotPublisher _publisher = new();
    private readonly CommandBus _commands = new();
    private readonly CancellationTokenSource _cts = new();
    private Thread? _thread;
    private long _tick;
    private bool _disposed;

    public Scheduler Scheduler => _scheduler;
    public SimWorld World => _world;
    public SnapshotPublisher Publisher => _publisher;
    public CommandBus Commands => _commands;
    public long TickNumber => Interlocked.Read(ref _tick);

    public void Start()
    {
        if (_thread is not null)
        {
            throw new InvalidOperationException("SimRuntime already started.");
        }
        _thread = new Thread(Loop)
        {
            IsBackground = true,
            Name = "SimThread",
        };
        _thread.Start();
    }

    private void Loop()
    {
        var token = _cts.Token;
        var stepTicks = Stopwatch.Frequency / SimConstants.TickRateHz;
        var nextTick = Stopwatch.GetTimestamp() + stepTicks;

        while (!token.IsCancellationRequested)
        {
            var current = Interlocked.Increment(ref _tick);
            var ctx = new TickContext(current, SimConstants.FixedDeltaSeconds);
            _scheduler.Tick(ctx);
            _publisher.Publish(new SimSnapshot(
                TickNumber: current,
                ElapsedSeconds: GameClock.SecondsAt(current),
                EntityCount: _world.EntityCount,
                Colonists: BuildColonistViews(),
                Spots: BuildSpotViews()));

            var now = Stopwatch.GetTimestamp();
            var remaining = nextTick - now;
            if (remaining > 0)
            {
                var ms = (int)(remaining * 1000 / Stopwatch.Frequency);
                if (ms > 0)
                {
                    if (token.WaitHandle.WaitOne(ms))
                    {
                        return;
                    }
                }
                else
                {
                    Thread.SpinWait(64);
                }
            }
            nextTick += stepTicks;
        }
    }

    private ColonistView[] BuildColonistViews()
    {
        var query = _world.Store.Query<Colonist, TilePosition, Needs, Job>();
        var views = new ColonistView[query.Count];
        var i = 0;
        foreach (var entity in query.Entities)
        {
            ref var p = ref entity.GetComponent<TilePosition>();
            ref var n = ref entity.GetComponent<Needs>();
            ref var j = ref entity.GetComponent<Job>();
            views[i++] = new ColonistView(
                entity.Id, p.MetersX, p.MetersY,
                n.Hunger, n.Thirst, n.Energy,
                j.Active, j.NeedKind);
        }
        return views;
    }

    private SpotView[] BuildSpotViews()
    {
        var query = _world.Store.Query<NeedSpot, TilePosition>();
        var views = new SpotView[query.Count];
        var i = 0;
        foreach (var entity in query.Entities)
        {
            ref var s = ref entity.GetComponent<NeedSpot>();
            ref var p = ref entity.GetComponent<TilePosition>();
            views[i++] = new SpotView(s.Kind, p.TileX, p.TileY);
        }
        return views;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _cts.Cancel();
        _thread?.Join();
        _cts.Dispose();
    }
}
