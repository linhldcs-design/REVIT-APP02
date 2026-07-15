using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using IsolatedFootingRebar.Services;
using IsolatedFootingRebar.Views;
using Nice3point.Revit.Toolkit;
using Nice3point.Revit.Toolkit.External;

namespace IsolatedFootingRebar.Commands;

/// <summary>
///     External command entry point. Bấm Ribbon → CHỌN MÓNG NGAY (trong API context) → mở dialog modeless
///     "Isolated Footing" (7 tab cấu hình). Ghi document qua ExternalEvent trong ViewModel.
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
            if (!licenseOk) { TaskDialog.Show("Vẽ Móng Đơn", licenseMsg); return; }

            var uiDoc = RevitContext.UiApplication.ActiveUIDocument;
            if (uiDoc is null)
            {
                TaskDialog.Show("IsolatedFootingRebar", "Không có tài liệu Revit đang mở.");
                return;
            }

            // Chọn móng NGAY khi bấm Ribbon (đang trong API context nên pick trực tiếp được).
            var foundation = new FoundationPicker().PickFoundation(uiDoc);
            if (foundation is null)
                return; // người dùng huỷ (ESC).

            var viewModel = Host.GetService<ViewModels.FootingRebarViewModel>();
            viewModel.SetFoundation(foundation);

            var view = new FootingRebarView(viewModel);
            new WindowInteropHelper(view) { Owner = RevitContext.UiApplication.MainWindowHandle };
            view.Show();
        }
        catch (Exception ex)
        {
            TaskDialog.Show("IsolatedFootingRebar error", ex.ToString());
            throw;
        }
    }
}
