using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace FootingDrawing.Addin.Configuration;

/// <summary>Cấu hình logging cho add-in (Serilog → Debug sink).</summary>
public static class LoggerConfiguration
{
    private const string LogTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}]: {Message:lj}{NewLine}{Exception}";

    public static Logger CreateDefaultLogger()
    {
        return new Serilog.LoggerConfiguration()
            .WriteTo.Debug(LogEventLevel.Debug, LogTemplate)
            .MinimumLevel.Debug()
            .CreateLogger();
    }
}
