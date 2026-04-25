using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace CowColonySim.Sim.Logging;

public static class SimLog
{
    private static Logger? _logger;

    public static ILogger Logger => _logger ?? Serilog.Log.Logger;

    public static void Configure(string? logFilePath = null, LogEventLevel minimumLevel = LogEventLevel.Information)
    {
        var config = new LoggerConfiguration()
            .MinimumLevel.Is(minimumLevel)
            .Enrich.WithProperty("scope", "sim")
            .WriteTo.Console();

        if (!string.IsNullOrWhiteSpace(logFilePath))
        {
            config = config.WriteTo.File(logFilePath, rollingInterval: RollingInterval.Day);
        }

        _logger = config.CreateLogger();
        Serilog.Log.Logger = _logger;
    }
}
