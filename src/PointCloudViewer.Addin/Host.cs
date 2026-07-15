using Microsoft.Extensions.DependencyInjection;
using PointCloudViewer.Addin.Configuration;
using PointCloudViewer.Addin.Services;
using PointCloudViewer.Addin.ViewModels;
using PointCloudViewer.Addin.Views;
using PointCloudViewer.Core.Caching;
using PointCloudViewer.Core.Processing;

namespace PointCloudViewer.Addin;

/// <summary>
///     Provides a host for the application's services and manages their lifetimes.
/// </summary>
public static class Host
{
    private static IServiceProvider? _serviceProvider;

    /// <summary>
    ///     Starts the host and configures the application's services.
    /// </summary>
    public static void Start()
    {
        var services = new ServiceCollection();

        services.AddSerilog();

        services.AddSingleton<ColorTransformService>();
        services.AddSingleton<RenderSettingsChangeClassifier>();
        services.AddSingleton<PointCloudSettingsStore>();
        services.AddSingleton<PointCloudRenderCoordinator>();
        services.AddSingleton<RevitPointCloudViewService>();
        services.AddSingleton<PointCloudDirectContext3DService>();

        services.AddTransient<PointCloudViewer_AddinViewModel>();
        services.AddTransient<PointCloudViewer_AddinView>();

        _serviceProvider = services.BuildServiceProvider();
    }

    /// <summary>
    ///     Get service of type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The type of service object to get.</typeparam>
    /// <exception cref="InvalidOperationException">There is no service of type <typeparamref name="T"/>.</exception>
    public static T GetService<T>() where T : class
    {
        return _serviceProvider!.GetRequiredService<T>();
    }
}
