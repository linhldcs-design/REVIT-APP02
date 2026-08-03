using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Nice3point.Revit.Toolkit.External;
using RevitAPP.Services.DwgExport;
using RevitAPP.ViewModels;
using RevitAPP.Views;
using Serilog;

namespace RevitAPP.Commands;

[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
public sealed class ExportSheetsToDwgCommand : ExternalCommand
{
    public override void Execute()
    {
        if (!LicenseCommandGate.Ensure("Xuất DWG Model")) return;
        var document = Application.ActiveUIDocument.Document;
        var stage = "đọc cấu hình DWG và Print Set";
        try
        {
            var catalog = DwgExportCatalog.Load(document);
            if (catalog.Setups.Count == 0 || catalog.PrintSets.Count == 0)
            {
                TaskDialog.Show("RevitAPP", "Project cần ít nhất một Export DWG Setup và một Print Set đã lưu.");
                return;
            }

            stage = "mở hộp thoại chọn file DWG";
            var viewModel = new DwgExportViewModel(
                catalog,
                new DwgOutputPathPicker(),
                AutoCadDwgPostProcessor.IsAvailable());
            var window = new DwgExportWindow(Application.MainWindowHandle, viewModel);
            if (window.ShowDialog() != true || viewModel.Result is null) return;

            stage = "xuất sheet và ghép file DWG";
            var output = new RevitDwgExportService().Export(document, viewModel.Result);
            TaskDialog.Show(
                "RevitAPP",
                $"Đã xuất {viewModel.Result.PrintSet.Sheets.Count} sheet vào một DWG duy nhất:\n{output}");
        }
        catch (Exception exception)
        {
            var cause = exception.GetBaseException();
            Log.Error(exception, "Export Print Set to single Model Space DWG failed at {Stage}", stage);
            TaskDialog.Show("RevitAPP", $"Không thể xuất DWG tại bước '{stage}':\n{cause.Message}");
        }
    }
}
