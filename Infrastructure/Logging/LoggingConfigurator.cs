using Serilog;
using Serilog.Events;

namespace NeZha_Desktop.Infrastructure.Logging;

public static class LoggingConfigurator
{
    public static void Configure()
    {
        var logRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NeZhaDesktop",
            "logs");

        Directory.CreateDirectory(logRoot);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(LogEventLevel.Information)
#if DEBUG
            .MinimumLevel.Debug()
#endif
            .Enrich.FromLogContext()
            .WriteTo.File(
                Path.Combine(logRoot, "app-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                shared: true)
            .CreateLogger();
    }
}

