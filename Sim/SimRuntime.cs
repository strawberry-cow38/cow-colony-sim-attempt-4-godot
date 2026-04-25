using System.Diagnostics;
using CowColonySim.Sim.Snapshots;
using CowColonySim.Sim.Systems;
using CowColonySim.Sim.World;

namespace CowColonySim.Sim;

public sealed class SimRuntime : IDisposable
{
    private readonly Scheduler _scheduler;
    private readonly SimWorld _world;
    private readonly Thread _thread;
    private readonly CancellationTokenSource _cts = new();
    private volatile SimSnapshot _latest = SimSnapshot.Empty;

    public SimWorld World => _world;
    public Scheduler Scheduler => _scheduler;
    public SimSnapshot LatestSnapshot => _latest;
    public bool IsRunning { get; private set; }

    public SimRuntime()
    {
        _world = new SimWorld();
        _scheduler = new Scheduler();
        _thread = new Thread(Loop) { Name = "SimThread", IsBackground = true };
    }

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }
        IsRunning = true;
        _thread.Start();
    }

    public void Stop()
    {
        if (!IsRunning)
        {
            return;
        }
        _cts.Cancel();
        _thread.Join();
        IsRunning = false;
    }

    private void Loop()
    {
        var token = _cts.Token;
        var sw = Stopwatch.StartNew();
        var nextTickTicks = sw.ElapsedTicks;
        var ticksPerStep = (long)(Stopwatch.Frequency * SimConstants.FixedDeltaSeconds);

        while (!token.IsCancellationRequested)
        {
            var now = sw.ElapsedTicks;
            if (now < nextTickTicks)
            {
                var remaining = nextTickTicks - now;
                var ms = (int)(remaining * 1000L / Stopwatch.Frequency);
                if (ms > 1)
                {
                    Thread.Sleep(ms - 1);
                }
                else
                {
                    Thread.SpinWait(64);
                }
                continue;
            }

            var report = _scheduler.TickOnce();
            _latest = new SimSnapshot(
                report.TickNumber,
                report.TickNumber * SimConstants.FixedDeltaSeconds);

            nextTickTicks += ticksPerStep;

            if (sw.ElapsedTicks - nextTickTicks > ticksPerStep * 5)
            {
                nextTickTicks = sw.ElapsedTicks + ticksPerStep;
            }
        }
    }

    public void Dispose()
    {
        Stop();
        _cts.Dispose();
    }
}
