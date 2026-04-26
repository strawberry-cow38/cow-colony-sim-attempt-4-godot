using System.Diagnostics;
using CowColonySim.Sim.Systems;

namespace CowColonySim.Sim;

// Owns the dedicated SimThread and the scheduler. Pre-pre-game: no entities
// yet, just a fixed-step tick loop other systems can hook into.
public sealed class SimRuntime : IDisposable
{
    private readonly Scheduler _scheduler = new();
    private readonly CancellationTokenSource _cts = new();
    private Thread? _thread;
    private long _tick;
    private bool _disposed;

    public Scheduler Scheduler => _scheduler;
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
