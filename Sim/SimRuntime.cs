using System.Diagnostics;
using CowColonySim.Sim.Commands;
using CowColonySim.Sim.Pathfinding;
using CowColonySim.Sim.Snapshots;
using CowColonySim.Sim.Systems;
using CowColonySim.Sim.Time;
using CowColonySim.Sim.World;
using CowColonySim.Sim.World.Components;
using CowColonySim.Sim.Zones;

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
    private int _speed = 1;
    private bool _disposed;

    public Scheduler Scheduler => _scheduler;
    public SimWorld World => _world;
    public SnapshotPublisher Publisher => _publisher;
    public CommandBus Commands => _commands;
    public long TickNumber => Interlocked.Read(ref _tick);

    // 0 = paused, otherwise tick-rate multiplier. Loop reads this each tick
    // so it can change live from the main thread.
    public int Speed
    {
        get => Volatile.Read(ref _speed);
        set => Volatile.Write(ref _speed, Math.Clamp(value, 0, 16));
    }

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
        var nextTick = Stopwatch.GetTimestamp();

        while (!token.IsCancellationRequested)
        {
            var speed = Speed;
            if (speed <= 0)
            {
                if (token.WaitHandle.WaitOne(20))
                {
                    return;
                }
                nextTick = Stopwatch.GetTimestamp();
                continue;
            }

            var stepTicks = Stopwatch.Frequency / (SimConstants.TickRateHz * speed);
            var current = Interlocked.Increment(ref _tick);
            var ctx = new TickContext(current, SimConstants.FixedDeltaSeconds);
            _scheduler.Tick(ctx);
            _publisher.Publish(new SimSnapshot(
                TickNumber: current,
                ElapsedSeconds: GameClock.SecondsAt(current),
                EntityCount: _world.EntityCount,
                Colonists: BuildColonistViews(),
                Spots: BuildSpotViews(),
                Paths: BuildPathViews(),
                Zones: BuildZoneViews(),
                Designations: BuildDesignationViews(),
                BlueprintGhosts: BuildBlueprintGhostViews()));

            nextTick += stepTicks;
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
            else if (-remaining > stepTicks * 4)
            {
                // Fell far behind (paused for a while or stalled). Resync to
                // wall clock instead of running a catch-up storm.
                nextTick = now;
            }
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

    private PathView[] BuildPathViews()
    {
        var query = _world.Store.Query<Colonist, PathFollower>();
        var list = new List<PathView>(query.Count);
        foreach (var entity in query.Entities)
        {
            ref var pf = ref entity.GetComponent<PathFollower>();
            if (!pf.PlayerForced) continue;
            if (pf.Tiles is null) continue;
            var remaining = pf.Tiles.Length - pf.Index;
            if (remaining <= 0) continue;
            var slice = new TileCoord[remaining];
            Array.Copy(pf.Tiles, pf.Index, slice, 0, remaining);
            list.Add(new PathView(entity.Id, slice));
        }
        return list.ToArray();
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

    private ZoneView[] BuildZoneViews()
    {
        var query = _world.Store.Query<Zone>();
        var views = new ZoneView[query.Count];
        var i = 0;
        foreach (var entity in query.Entities)
        {
            ref var z = ref entity.GetComponent<Zone>();
            var priority = entity.HasComponent<StockpileSettings>()
                ? entity.GetComponent<StockpileSettings>().Priority : 0;
            var cropDefId = entity.HasComponent<FarmSettings>()
                ? entity.GetComponent<FarmSettings>().CropDefId : 0;
            views[i++] = new ZoneView(
                z.ZoneId, z.Type,
                z.Rect.MinX, z.Rect.MinY, z.Rect.MaxX, z.Rect.MaxY,
                z.Name, priority, cropDefId);
        }
        return views;
    }

    private DesignationView[] BuildDesignationViews()
    {
        var query = _world.Store.Query<Designation, TilePosition>();
        var views = new DesignationView[query.Count];
        var i = 0;
        foreach (var entity in query.Entities)
        {
            ref var d = ref entity.GetComponent<Designation>();
            ref var p = ref entity.GetComponent<TilePosition>();
            views[i++] = new DesignationView(entity.Id, d.Kind, p.TileX, p.TileY);
        }
        return views;
    }

    private BlueprintGhostView[] BuildBlueprintGhostViews()
    {
        var query = _world.Store.Query<BlueprintGhost>();
        var views = new BlueprintGhostView[query.Count];
        var i = 0;
        foreach (var entity in query.Entities)
        {
            ref var g = ref entity.GetComponent<BlueprintGhost>();
            views[i++] = new BlueprintGhostView(
                entity.Id, g.DefId, g.OriginTileX, g.OriginTileY,
                g.Rotation, g.BuildProgress);
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
