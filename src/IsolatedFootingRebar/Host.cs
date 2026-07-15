using IsolatedFootingRebar.ViewModels;
using Serilog;

namespace IsolatedFootingRebar;

/// <summary>
///     Lightweight service host for the add-in. Avoids Microsoft.Extensions.DependencyInjection to reduce
///     assembly version conflicts inside Revit when other add-ins preload different DI versions.
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

        if (typeof(T) == typeof(FootingRebarViewModel))
            return (T)(object)new FootingRebarViewModel(GetService<ILogger>());

        throw new InvalidOperationException($"No service of type {typeof(T).FullName} is registered.");
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs args)
    {
        if (args.ExceptionObject is Exception exception)
            GetService<ILogger>().Fatal(exception, "Domain unhandled exception");
    }
}
