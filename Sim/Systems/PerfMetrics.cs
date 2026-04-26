using System.Collections.Concurrent;
using System.Diagnostics;

namespace CowColonySim.Sim.Systems;

// Thread-safe perf counters for the sim scheduler. Producer = SimThread,
// consumer = main (Godot) thread. All timings stored as Stopwatch ticks,
// converted to ms on read.
public sealed class PerfMetrics
{
    private readonly ConcurrentDictionary<string, SystemSample> _systems = new();
    private long _tickTotalTicks;

    public void RecordSystem(string name, long elapsedStopwatchTicks)
    {
        var s = _systems.GetOrAdd(name, _ => new SystemSample());
        s.Update(elapsedStopwatchTicks);
    }

    public void RecordTickTotal(long elapsedStopwatchTicks) =>
        Volatile.Write(ref _tickTotalTicks, elapsedStopwatchTicks);

    public double LastTickMs => TicksToMs(Volatile.Read(ref _tickTotalTicks));

    public IReadOnlyDictionary<string, SystemSample> Systems => _systems;

    public static double TicksToMs(long ticks) =>
        ticks * 1000.0 / Stopwatch.Frequency;

    public sealed class SystemSample
    {
        private long _last;
        private long _max;
        private long _ewmaQ16; // fixed-point Q16 EWMA in stopwatch ticks

        public void Update(long ticks)
        {
            Volatile.Write(ref _last, ticks);
            var cur = Volatile.Read(ref _max);
            while (ticks > cur)
            {
                var prev = Interlocked.CompareExchange(ref _max, ticks, cur);
                if (prev == cur) break;
                cur = prev;
            }
            // EWMA with alpha = 1/16.
            var prevEwma = Volatile.Read(ref _ewmaQ16);
            var sample = ticks << 16;
            var next = prevEwma + ((sample - prevEwma) >> 4);
            Volatile.Write(ref _ewmaQ16, next);
        }

        public double LastMs => TicksToMs(Volatile.Read(ref _last));
        public double MaxMs => TicksToMs(Volatile.Read(ref _max));
        public double AvgMs => TicksToMs(Volatile.Read(ref _ewmaQ16) >> 16);

        public void ResetMax() => Volatile.Write(ref _max, 0);
    }
}
