using System.Diagnostics;
using CowColonySim.Sim.Commands;
using CowColonySim.Sim.Designations;
using CowColonySim.Sim.Items;
using CowColonySim.Sim.Logging;
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

    // Bootstrap registers the LightingSystem then assigns it here so the
    // snapshot loop can clone its grid + sun fraction each tick. Null
    // until set; LightingView falls back to Empty in that case.
    public LightingSystem? Lighting { get; set; }

    // Same pattern for the weather grids — temperature + rainfall.
    // WeatherView falls back to Empty when null.
    public WeatherSystem? Weather { get; set; }

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
            try
            {
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
                    BlueprintGhosts: BuildBlueprintGhostViews(),
                    Trees: BuildTreeViews(),
                    Boulders: BuildBoulderViews(),
                    Items: BuildItemViews(),
                    Structures: BuildStructureViews(),
                    TreeFalls: _world.DrainTreeFalls(),
                    Lighting: BuildLightingView(),
                    Weather: BuildWeatherView()));
            }
            catch (Exception ex)
            {
                // Crash on the sim thread closes the Godot console before
                // anything reaches stdout. Log to file so post-mortem is
                // possible, then rethrow so we don't silently corrupt state.
                SimLog.Logger.Fatal(ex, "Sim tick {Tick} threw", current);
                Serilog.Log.CloseAndFlush();
                throw;
            }

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
        var query = _world.Store.Query<Colonist, TilePosition, Needs, Job, WorkJob>();
        var views = new ColonistView[query.Count];
        var i = 0;
        foreach (var entity in query.Entities)
        {
            ref var p = ref entity.GetComponent<TilePosition>();
            ref var n = ref entity.GetComponent<Needs>();
            ref var j = ref entity.GetComponent<Job>();
            ref var w = ref entity.GetComponent<WorkJob>();

            var invView = Array.Empty<InventoryStackView>();
            var carryWeight = 0f;
            var maxWeight = 0f;
            var carryBulk = 0f;
            var maxBulk = 0f;
            if (entity.HasComponent<Inventory>() && entity.HasComponent<CarryCaps>())
            {
                ref var inv = ref entity.GetComponent<Inventory>();
                ref var caps = ref entity.GetComponent<CarryCaps>();
                carryWeight = Items.InventoryOps.TotalWeight(inv);
                maxWeight = Items.InventoryOps.MaxWeight(caps, inv);
                carryBulk = Items.InventoryOps.TotalBulk(inv);
                maxBulk = Items.InventoryOps.MaxBulk(caps, inv);
                if (inv.Stacks is not null && inv.Stacks.Count > 0)
                {
                    invView = new InventoryStackView[inv.Stacks.Count];
                    for (var s = 0; s < inv.Stacks.Count; s++)
                    {
                        var stack = inv.Stacks[s];
                        var def = Items.ItemCatalog.Get(stack.DefId);
                        invView[s] = new InventoryStackView(
                            s, stack.DefId, def.DisplayName, def.Description,
                            stack.Count, def.Weight, def.Bulk, def.SellValue,
                            stack.Equipped, stack.Locked, def.IsWeapon, def.IsClothing,
                            stack.WrappedDefId ?? string.Empty,
                            stack.Material, stack.Quality, stack.Durability);
                    }
                }
            }

            var drafted = entity.HasComponent<Drafted>() && entity.GetComponent<Drafted>().Active;
            var prios = new byte[WorkTypes.Count];
            if (entity.HasComponent<WorkPriorities>())
            {
                ref var wp = ref entity.GetComponent<WorkPriorities>();
                for (var ti = 0; ti < WorkTypes.Count; ti++)
                {
                    prios[ti] = wp.Get((WorkType)ti);
                }
            }
            views[i++] = new ColonistView(
                entity.Id, p.MetersX, p.MetersY,
                n.Hunger, n.Thirst, n.Energy,
                j.Active, j.NeedKind,
                w.Active, w.Kind, w.Carrying, w.CarryKind, w.CarryCount,
                carryWeight, maxWeight, carryBulk, maxBulk, invView, drafted, prios);
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
            var priority = 0;
            var allowedMask = StockpileFilter.DefaultMask;
            if (entity.HasComponent<StockpileSettings>())
            {
                ref var s = ref entity.GetComponent<StockpileSettings>();
                priority = s.Priority;
                allowedMask = s.AllowedKindsMask;
            }
            var cropDefId = 0;
            var allowSow = false;
            var allowHarv = false;
            if (entity.HasComponent<FarmSettings>())
            {
                ref var f = ref entity.GetComponent<FarmSettings>();
                cropDefId = f.CropDefId;
                allowSow = f.AllowSowing;
                allowHarv = f.AllowHarvest;
            }
            views[i++] = new ZoneView(
                z.ZoneId, z.Type,
                z.Rect.MinX, z.Rect.MinY, z.Rect.MaxX, z.Rect.MaxY,
                z.Mask, z.Name, priority, cropDefId, allowSow, allowHarv, allowedMask);
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

    private TreeView[] BuildTreeViews()
    {
        var activeChops = new HashSet<int>();
        var workQuery = _world.Store.Query<Colonist, WorkJob, Job, TilePosition>();
        foreach (var entity in workQuery.Entities)
        {
            ref var w = ref entity.GetComponent<WorkJob>();
            if (!w.Active || w.Kind != WorkKind.ChopTree || w.TargetEntityId == 0) continue;
            ref var j = ref entity.GetComponent<Job>();
            if (j.Active) continue;
            ref var pos = ref entity.GetComponent<TilePosition>();
            if (Math.Abs(pos.TileX - w.TargetTileX) > 1 || Math.Abs(pos.TileY - w.TargetTileY) > 1) continue;
            activeChops.Add(w.TargetEntityId);
        }

        var query = _world.Store.Query<Tree, TilePosition>();
        var views = new TreeView[query.Count];
        var i = 0;
        foreach (var entity in query.Entities)
        {
            ref var t = ref entity.GetComponent<Tree>();
            ref var p = ref entity.GetComponent<TilePosition>();
            var growth = entity.HasComponent<Plant>() ? entity.GetComponent<Plant>().Growth : 100f;
            views[i++] = new TreeView(entity.Id, p.TileX, p.TileY, t.Health, t.VariantSeed, activeChops.Contains(entity.Id), t.HitCount, growth);
        }
        return views;
    }

    private BoulderView[] BuildBoulderViews()
    {
        var activeMines = new HashSet<int>();
        var workQuery = _world.Store.Query<Colonist, WorkJob, Job, TilePosition>();
        foreach (var entity in workQuery.Entities)
        {
            ref var w = ref entity.GetComponent<WorkJob>();
            if (!w.Active || w.Kind != WorkKind.Mine || w.TargetEntityId == 0) continue;
            ref var j = ref entity.GetComponent<Job>();
            if (j.Active) continue;
            ref var pos = ref entity.GetComponent<TilePosition>();
            if (Math.Abs(pos.TileX - w.TargetTileX) > 1 || Math.Abs(pos.TileY - w.TargetTileY) > 1) continue;
            activeMines.Add(w.TargetEntityId);
        }

        var query = _world.Store.Query<Boulder, TilePosition>();
        var views = new BoulderView[query.Count];
        var i = 0;
        foreach (var entity in query.Entities)
        {
            ref var b = ref entity.GetComponent<Boulder>();
            ref var p = ref entity.GetComponent<TilePosition>();
            views[i++] = new BoulderView(
                entity.Id, p.TileX, p.TileY, b.Health, b.VariantSeed, b.Variant,
                activeMines.Contains(entity.Id), b.HitCount);
        }
        return views;
    }

    private ItemView[] BuildItemViews()
    {
        var query = _world.Store.Query<Item, TilePosition>();
        var views = new ItemView[query.Count];
        var i = 0;
        foreach (var entity in query.Entities)
        {
            ref var it = ref entity.GetComponent<Item>();
            ref var p = ref entity.GetComponent<TilePosition>();
            string? miniDef = null;
            var miniRot = 0;
            if (entity.HasComponent<MinifiedThing>())
            {
                ref var m = ref entity.GetComponent<MinifiedThing>();
                miniDef = m.DefId;
                miniRot = m.Rotation;
            }
            views[i++] = new ItemView(entity.Id, it.Kind, it.Count, it.Capacity, p.TileX, p.TileY, it.Forbidden, miniDef, miniRot);
        }
        return views;
    }

    private LightingView BuildLightingView()
    {
        if (Lighting is null) return LightingView.Empty;
        return new LightingView(
            Lighting.Grid.Width, Lighting.Grid.Height,
            Lighting.Grid.Clone(), Lighting.SunFraction);
    }

    private WeatherView BuildWeatherView()
    {
        if (Weather is null) return WeatherView.Empty;
        return new WeatherView(
            Weather.Temperature.Width, Weather.Temperature.Height,
            Weather.Temperature.Clone(), Weather.Rainfall.Clone(),
            Weather.CurrentCelsius, Weather.CurrentRainfall,
            Weather.Climate.AnnualRainfallMm,
            Weather.CurrentWindRad, Weather.CurrentWindSpeed);
    }

    private BlueprintGhostView[] BuildBlueprintGhostViews()
    {
        var query = _world.Store.Query<BlueprintGhost>();
        var views = new BlueprintGhostView[query.Count];
        var i = 0;
        foreach (var entity in query.Entities)
        {
            ref var g = ref entity.GetComponent<BlueprintGhost>();
            var def = Blueprints.BlueprintCatalog.Get(g.DefId);
            var required = 0;
            foreach (var m in def.MaterialsOrEmpty) required += m.Count;
            views[i++] = new BlueprintGhostView(
                entity.Id, g.DefId, g.OriginTileX, g.OriginTileY,
                g.Rotation, g.BaseLayer, g.BuildProgress,
                g.MaterialDeposited, required);
        }
        return views;
    }

    private StructureView[] BuildStructureViews()
    {
        var query = _world.Store.Query<Structure, TilePosition>();
        var views = new StructureView[query.Count];
        var i = 0;
        foreach (var entity in query.Entities)
        {
            ref var s = ref entity.GetComponent<Structure>();
            ref var p = ref entity.GetComponent<TilePosition>();
            views[i++] = new StructureView(entity.Id, s.DefId, p.TileX, p.TileY, s.Rotation, s.BaseLayer);
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
