using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using BeamRebarPro.Services;
using BeamRebarPro.ViewModels;
using BeamRebarPro.Views;
using Nice3point.Revit.Toolkit;
using Nice3point.Revit.Toolkit.External;

namespace BeamRebarPro.Commands;

/// <summary>
///     External command entry point. Quy trình: bấm Ribbon → CHỌN DẦM NGAY (trong API context) →
///     đọc thông số nhịp → rồi mới mở dialog Quick Setting với dữ liệu sẵn sàng.
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
            if (!licenseOk) { TaskDialog.Show("Vẽ Thép Dầm", licenseMsg); return; }

            var uiDoc = RevitContext.UiApplication.ActiveUIDocument;
            if (uiDoc is null)
            {
                TaskDialog.Show("BeamRebarPro", "Không có tài liệu Revit đang mở.");
                return;
            }

            // Chọn dầm NGAY khi bấm Ribbon (đang trong API context nên pick trực tiếp được).
            var beams = new BeamPicker().PickBeams(uiDoc);
            if (beams.Count == 0)
                return; // người dùng huỷ (ESC) — không mở dialog.

            var spans = BeamSpanReader.ReadSpans(uiDoc.Document, beams);

            var viewModel = Host.GetService<BeamRebarProViewModel>();
            viewModel.SetPickedBeams(beams, spans);

            // Nếu dầm đã được cấu hình trước → tự load lại thông số đã lưu (khỏi nhập lại).
            var saved = BeamConfigStore.Load(beams.Select(b => b.Id.ToValue()).ToList());
            if (saved is not null)
                viewModel.LoadSavedConfig(saved);

            var view = new BeamRebarProView(viewModel);
            new WindowInteropHelper(view) { Owner = RevitContext.UiApplication.MainWindowHandle };
            view.Show();
        }
        catch (Exception ex)
        {
            TaskDialog.Show("BeamRebarPro error", ex.ToString());
            throw;
        }
    }
}
