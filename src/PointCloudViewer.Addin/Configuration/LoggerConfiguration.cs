using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace PointCloudViewer.Addin.Configuration;

/// <summary>
///     Application logging configuration.
/// </summary>
/// <example>
/// <code lang="csharp">
/// public class Class(ILogger logger)
/// {
///     private void Execute()
///     {
///         logger.Information("Message");
///     }
/// }
/// </code>
/// </example>
public static class LoggerConfiguration
{
    private const string LogTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}]: {Message:lj}{NewLine}{Exception}";

    extension(IServiceCollection services)
    {
        public void AddSerilog()
        {
            var logger = CreateDefaultLogger();
            services.AddSingleton<ILogger>(logger);

            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        }
    }

    private static Logger CreateDefaultLogger()
    {
        return new Serilog.LoggerConfiguration()
            .WriteTo.Debug(LogEventLevel.Debug, LogTemplate)
            .MinimumLevel.Debug()
            .CreateLogger();
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs args)
    {
        var exception = (Exception)args.ExceptionObject;
        var logger = Host.GetService<ILogger>();
        logger.Fatal(exception, "Domain unhandled exception");
    }
}