using CowColonySim.Sim.Logging;
using Xunit;

namespace CowColonySim.Tests;

public class SimLogTests
{
    [Fact]
    public void Configure_is_idempotent()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cowsim-logs-" + Guid.NewGuid().ToString("N"));
        try
        {
            SimLog.Configure(dir);
            var first = SimLog.Logger;
            SimLog.Configure(dir);
            var second = SimLog.Logger;
            Assert.Same(first, second);
        }
        finally
        {
            SimLog.Reset();
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public void Configure_creates_log_directory_and_file()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cowsim-logs-" + Guid.NewGuid().ToString("N"));
        try
        {
            SimLog.Configure(dir);
            SimLog.Logger.Information("hello {N}", 42);
            SimLog.Reset();

            Assert.True(Directory.Exists(dir));
            var files = Directory.GetFiles(dir, "sim-*.log");
            Assert.NotEmpty(files);
            var contents = File.ReadAllText(files[0]);
            Assert.Contains("hello 42", contents);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
