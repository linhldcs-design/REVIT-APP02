using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using Nice3point.Revit.Toolkit;
using Nice3point.Revit.Toolkit.External;
using WallRebar.Services;
using WallRebar.ViewModels;
using WallRebar.Views;

namespace WallRebar.Commands;

/// <summary>
///     External command entry point. Bấm Ribbon → CHỌN TƯỜNG NGAY (trong API context) → mở dialog modeless
///     "Wall Rebar". Ghi document qua ExternalEvent trong ViewModel.
///     LƯU Ý: command phải tự đứng độc lập — Add-in Manager load thẳng IExternalCommand, KHÔNG chạy OnStartup.
/// </summary>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
public class StartupCommand : ExternalCommand
{
    public override void Execute()
    {
        try
        {
            var (licenseOk, licenseMsg) = RevitAPP.Licensing.LicenseService.EnsureValid();
            if (!licenseOk) { TaskDialog.Show("Vẽ Thép Tường", licenseMsg); return; }

            // Host có thể chưa Start nếu load qua Add-in Manager (OnStartup không chạy) → đảm bảo khởi tạo.
            Host.Start();

            var uiDoc = RevitContext.UiApplication.ActiveUIDocument;
            if (uiDoc is null)
            {
                TaskDialog.Show("WallRebar", "Không có tài liệu Revit đang mở.");
                return;
            }

            // Chọn tường NGAY khi bấm Ribbon (đang trong API context nên pick trực tiếp được).
            var wall = new WallPicker().PickWall(uiDoc);
            if (wall is null)
                return; // người dùng huỷ (ESC).

            var viewModel = Host.GetService<WallRebarViewModel>();
            viewModel.SetWall(wall);

            var view = new WallRebarView(viewModel);
            new WindowInteropHelper(view) { Owner = RevitContext.UiApplication.MainWindowHandle };
            view.Show();
        }
        catch (Exception ex)
        {
            TaskDialog.Show("WallRebar error", ex.ToString());
            throw;
        }
    }
}
