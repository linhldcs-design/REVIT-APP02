using System.IO;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace BeamRebarPro.Configuration;

/// <summary>
///     Application logging configuration.
/// </summary>
public static class LoggerConfiguration
{
    private const string LogTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}]: {Message:lj}{NewLine}{Exception}";

    /// <summary>Thư mục log, cạnh add-in để người dùng lấy được khi cần chẩn đoán.</summary>
    public static string LogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BeamRebarPro", "logs");

    public static Logger CreateDefaultLogger()
    {
        Directory.CreateDirectory(LogDirectory);

        return new Serilog.LoggerConfiguration()
            .WriteTo.Debug(LogEventLevel.Debug, LogTemplate)
            // Ghi ra file để chẩn đoán được ngoài môi trường gỡ lỗi.
            .WriteTo.File(
                Path.Combine(LogDirectory, "beamrebar-.log"),
                LogEventLevel.Debug,
                LogTemplate,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 5)
            .MinimumLevel.Debug()
            .CreateLogger();
    }
}
