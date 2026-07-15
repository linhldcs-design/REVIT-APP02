using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using Nice3point.Revit.Toolkit.External;
using RevitAPP.Services.Updates;
using RevitAPP.Licensing;

namespace RevitAPP.Commands;

[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
public sealed class CheckForUpdatesCommand : ExternalCommand
{
    public override void Execute()
    {
        var (licenseOk, licenseMessage) = LicenseService.EnsureValid();
        if (!licenseOk)
        {
            TaskDialog.Show("RevitAPP Update", licenseMessage);
            return;
        }

        try
        {
            var result = new RevitAppUpdateService().CheckAndStageAsync(Application.Application.VersionNumber)
                .GetAwaiter().GetResult();
            if (!result.UpdateAvailable)
            {
                TaskDialog.Show("RevitAPP Update", $"Bạn đang dùng bản mới nhất ({result.CurrentVersion}).");
                return;
            }

            var dialog = new TaskDialog("RevitAPP Update")
            {
                MainInstruction = $"Đã tải RevitAPP {result.LatestVersion}",
                MainContent = "Nhấn Cập nhật, sau đó đóng Revit. Bộ cập nhật sẽ tự thay file và giữ bản backup.",
                ExpandedContent = result.Notes ?? string.Empty,
                CommonButtons = TaskDialogCommonButtons.Cancel
            };
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Cập nhật sau khi đóng Revit");
            if (dialog.Show() == TaskDialogResult.CommandLink1 && result.PendingPath != null &&
                !RevitAppUpdateService.LaunchInstaller(result.PendingPath))
                TaskDialog.Show("RevitAPP Update", "Không tìm thấy RevitAPP.Updater.exe trong thư mục cài đặt.");
        }
        catch (Exception ex)
        {
            TaskDialog.Show("RevitAPP Update", "Không thể kiểm tra cập nhật: " + ex.Message);
        }
    }
}
