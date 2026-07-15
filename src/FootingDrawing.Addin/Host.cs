using Serilog;

namespace FootingDrawing.Addin;

/// <summary>
///     Service host nhẹ cho add-in. Tránh Microsoft.Extensions.DependencyInjection để giảm xung đột
///     version assembly khi Revit đã nạp add-in khác dùng DI version khác (theo tiền lệ IsolatedFootingRebar).
/// </summary>
public static class Host
{
    private static ILogger? _logger;

    public static void Start()
    {
        _logger = Configuration.LoggerConfiguration.CreateDefaultLogger();
        Log.Logger = _logger;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
    }

    public static ILogger Logger => _logger ??= Configuration.LoggerConfiguration.CreateDefaultLogger();

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs args)
    {
        if (args.ExceptionObject is Exception exception)
            Logger.Fatal(exception, "Domain unhandled exception");
    }
}
