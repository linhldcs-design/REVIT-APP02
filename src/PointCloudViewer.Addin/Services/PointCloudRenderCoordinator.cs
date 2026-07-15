using PointCloudViewer.Core.Caching;
using Serilog;

namespace PointCloudViewer.Addin.Services;

public sealed class PointCloudRenderCoordinator(PointCloudSettingsStore settingsStore, ILogger logger)
{
    private bool _isRegistered;

    public string Status { get; private set; } = "DirectContext3D renderer is not registered.";

    public void Register()
    {
        if (_isRegistered)
        {
            return;
        }

        _isRegistered = true;
        settingsStore.SettingsChanged += OnSettingsChanged;
        Status = "Render coordinator is ready. DirectContext3D server hook is prepared for Phase 04.";
        logger.Information("Point cloud render coordinator registered");
    }

    public void Unregister()
    {
        if (!_isRegistered)
        {
            return;
        }

        settingsStore.SettingsChanged -= OnSettingsChanged;
        _isRegistered = false;
        Status = "Render coordinator stopped.";
        logger.Information("Point cloud render coordinator unregistered");
    }

    public void RequestUpdate()
    {
        logger.Information("Point cloud render update requested");
        Status = "Render update requested. Point source and DirectContext3D draw path are pending.";
    }

    private void OnSettingsChanged(object? sender, RenderSettingsChangedEventArgs args)
    {
        var action = args.Impact == RenderSettingsChangeImpact.RebuildBatches ? "rebuild batches" : "redraw";
        Status = $"Settings changed: {action}.";
        logger.Information("Point cloud settings changed; render action: {Action}", action);
    }
}
