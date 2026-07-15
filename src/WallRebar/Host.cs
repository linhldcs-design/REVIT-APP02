using Serilog;
using WallRebar.ViewModels;

namespace WallRebar;

/// <summary>
///     Lightweight service host cho add-in. Tránh Microsoft.Extensions.DependencyInjection để giảm xung đột
///     version assembly bên trong Revit khi add-in khác preload DI version khác.
/// </summary>
public static class Host
{
    private static ILogger? _logger;

    public static void Start()
    {
        _logger = Configuration.LoggerConfiguration.CreateDefaultLogger();
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
    }

    public static T GetService<T>() where T : class
    {
        if (typeof(T) == typeof(ILogger))
            return (T)(_logger ??= Configuration.LoggerConfiguration.CreateDefaultLogger());

        if (typeof(T) == typeof(WallRebarViewModel))
            return (T)(object)new WallRebarViewModel(GetService<ILogger>());

        throw new InvalidOperationException($"No service of type {typeof(T).FullName} is registered.");
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs args)
    {
        if (args.ExceptionObject is Exception exception)
            GetService<ILogger>().Fatal(exception, "Domain unhandled exception");
    }
}
