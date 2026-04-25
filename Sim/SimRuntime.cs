using System.Diagnostics;
using CowColonySim.Sim.Climate;
using CowColonySim.Sim.Snapshots;
using CowColonySim.Sim.Systems;
using CowColonySim.Sim.Time;
using CowColonySim.Sim.World;

namespace CowColonySim.Sim;

public sealed class SimRuntime : IDisposable
{
    private readonly Scheduler _scheduler;
    private readonly SimWorld _world;
    private readonly SpeedController _speed;
    private readonly Thread _thread;
    private readonly CancellationTokenSource _cts = new();
    private volatile SimSnapshot _latest = SimSnapshot.Empty;

    public SimWorld World => _world;
    public Scheduler Scheduler => _scheduler;
    public SpeedController Speed => _speed;
    public SimSnapshot LatestSnapshot => _latest;
    public bool IsRunning { get; private set; }
    public ClimateState? Climate { get; set; }

    public SimRuntime()
    {
        _world = new SimWorld();
        _scheduler = new Scheduler();
        _speed = new SpeedController();
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

        while (!token.IsCancellationRequested)
        {
            var speed = _speed.Current;

            if (speed == SimSpeed.Paused)
            {
                Thread.Sleep(8);
                nextTickTicks = sw.ElapsedTicks;
                continue;
            }

            var ticksPerStep = Stopwatch.Frequency / _speed.TargetTicksPerSecond;
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

            _scheduler.TickOnce();
            var climate = Climate?.Current ?? ClimateSnapshot.Empty;
            _latest = SimSnapshot.FromTick(_scheduler.CurrentTick, speed, climate);

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
