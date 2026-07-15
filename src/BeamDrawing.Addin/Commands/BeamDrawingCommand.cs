using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using BeamDrawing.Addin.Services;
using BeamDrawing.Addin.ViewModels;
using BeamDrawing.Addin.Views;
using Nice3point.Revit.Toolkit.External;
using Serilog;

namespace BeamDrawing.Addin.Commands;

/// <summary>
///     Tạo bản vẽ chi tiết thép dầm: chọn dầm → sinh Sectional Elevation + Cross Section view,
///     đặt lên sheet theo cấu hình. Self-contained: tự khởi tạo logger trong Execute() vì khi nạp
///     qua Add-in Manager, Application.OnStartup KHÔNG chạy.
/// </summary>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
public class BeamDrawingCommand : ExternalCommand
{
    public override void Execute()
    {
        LoggerSetup.EnsureConfigured();
        var uiDocument = Application.ActiveUIDocument;
        var document = uiDocument.Document;

        // 1. Pick dầm.
        var beams = new BeamPicker().PickBeams(uiDocument, out var pickError);
        if (beams.Count == 0)
        {
            if (!string.IsNullOrEmpty(pickError)) TaskDialog.Show("Beam Drawing", pickError);
            return;
        }

        // 2. Nạp nguồn dữ liệu + cấu hình qua dialog.
        var resources = new ProjectResourceProvider().LoadResources(document);
        var viewModel = new BeamDrawingViewModel(resources, new JsonSettingStore());
        var view = new BeamDrawingView(viewModel);
        new WindowInteropHelper(view) { Owner = Application.MainWindowHandle };

        if (view.ShowDialog() != true || viewModel.Result == null) return;

        // 3. Sinh bản vẽ.
        try
        {
            var result = new BeamDrawingOrchestrator().Generate(document, beams, viewModel.Result);

            var message = $"Đã tạo {result.TotalViews} view ({result.SectionViewIds.Count} mặt cắt dọc, " +
                          $"{result.CrossSectionViewIds.Count} mặt cắt ngang) cho {beams.Count} dầm.";
            if (result.Warnings.Count > 0)
                message += "\n\nCảnh báo:\n- " + string.Join("\n- ", result.Warnings.Distinct());

            TaskDialog.Show("Beam Drawing", message);
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Tạo bản vẽ dầm thất bại");
            TaskDialog.Show("Beam Drawing", "Lỗi khi tạo bản vẽ: " + exception.Message);
        }
    }
}
