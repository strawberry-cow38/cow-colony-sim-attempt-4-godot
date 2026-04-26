using Serilog;
using Serilog.Events;

namespace CowColonySim.Sim.Logging;

// Single configuration point for Serilog. Sim and Game both write through
// SimLog.Logger so we get one unified timeline (console + rolling file).
// Configure() is idempotent and safe to call from headless tests.
public static class SimLog
{
    private static readonly object _gate = new();
    private static bool _configured;

    public static ILogger Logger { get; private set; } = Serilog.Log.Logger;

    public static void Configure(string? logDirectory = null)
    {
        lock (_gate)
        {
            if (_configured)
            {
                return;
            }

            var dir = logDirectory ?? Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(dir);

            Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .Enrich.FromLogContext()
                .WriteTo.Console(
                    restrictedToMinimumLevel: LogEventLevel.Information,
                    outputTemplate: "[{Timestamp:HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}")
                .WriteTo.File(
                    path: Path.Combine(dir, "sim-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();

            Serilog.Log.Logger = Logger;
            _configured = true;
        }
    }

    public static void Reset()
    {
        lock (_gate)
        {
            Serilog.Log.CloseAndFlush();
            Logger = Serilog.Log.Logger;
            _configured = false;
        }
    }
}
