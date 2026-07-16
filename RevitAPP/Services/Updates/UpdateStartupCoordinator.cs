using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using RevitAPP.Licensing;
using Serilog;

namespace RevitAPP.Services.Updates;

public static class UpdateStartupCoordinator
{
#if !DEBUG
    private static readonly object Sync = new();
    private static UpdateCheckResult? _result;
    private static bool _handled;
#endif

    public static void Start(UIControlledApplication application)
    {
#if DEBUG
        // Local development builds must not be replaced by the latest public release on startup.
        return;
#else
        application.Idling += OnIdling;
        var revitYear = application.ControlledApplication.VersionNumber;
        _ = Task.Run(async () =>
        {
            try
            {
                var (valid, _) = LicenseService.EnsureValid();
                if (!valid) return;
                var result = await new RevitAppUpdateService().CheckAndStageAsync(revitYear);
                if (result.UpdateAvailable && result.Staged)
                    lock (Sync) _result = result;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Automatic RevitAPP update check failed");
            }
        });
#endif
    }

#if !DEBUG
    private static void OnIdling(object? sender, IdlingEventArgs args)
    {
        UpdateCheckResult? result;
        lock (Sync)
        {
            if (_handled || _result == null) return;
            _handled = true;
            result = _result;
        }

        var dialog = new TaskDialog("RevitAPP Update")
        {
            MainInstruction = $"Có bản RevitAPP {result.LatestVersion}",
            MainContent = "Bản mới đã tải và xác minh. Chọn cập nhật rồi đóng Revit để hoàn tất.",
            ExpandedContent = result.Notes ?? string.Empty,
            CommonButtons = TaskDialogCommonButtons.Cancel
        };
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Cập nhật sau khi đóng Revit");
        if (dialog.Show() == TaskDialogResult.CommandLink1 && result.PendingPath != null)
            RevitAppUpdateService.LaunchInstaller(result.PendingPath);
    }
#endif
}
