using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Nice3point.Revit.Toolkit.External;
using RevitAPP.Core.Models.CadGrid;
using RevitAPP.Core.Services;
using RevitAPP.Services.CadGrid;
using RevitAPP.ViewModels;
using RevitAPP.Views;
using Serilog;

namespace RevitAPP.Commands;

/// <summary>
/// Creates Revit grids from a CAD line selection. Every selected line is offered for
/// review — including diagonals, which no two-family network analysis can describe — and
/// the user chooses which become grids and what they are called.
/// </summary>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
public sealed class CreateGridFromCadCommand : ExternalCommand
{
    private const string Title = "Tạo Lưới từ Cad";

    public override void Execute()
    {
        if (!LicenseCommandGate.Ensure(Title)) return;

        var uiDocument = Application.ActiveUIDocument;
        var document = uiDocument.Document;
        if (document.IsFamilyDocument)
        {
            TaskDialog.Show(Title, "Lệnh chỉ sử dụng trong project Revit.");
            return;
        }

        if (document.ActiveView is not ViewPlan viewPlan
            || viewPlan.IsTemplate
            || viewPlan.GenLevel is null
            || !IsSupportedPlan(viewPlan))
        {
            TaskDialog.Show(Title, "Hãy mở một mặt bằng có Level trước khi chạy lệnh.");
            return;
        }

        var package = ChooseSource();
        if (package is null) return;

        var preview = CadGridPreviewBuilder.Build(package);
        if (!preview.IsValid)
        {
            TaskDialog.Show(
                Title,
                "Không đọc được lưới từ dữ liệu AutoCAD.\n\n" + preview.Error);
            return;
        }

        try
        {
            var viewModel = new CadGridReviewViewModel(package, preview, Reselect);
            var window = new CadGridReviewWindow(viewModel);
            new WindowInteropHelper(window) { Owner = Application.MainWindowHandle };
            if (window.ShowDialog() != true) return;

            var selected = viewModel.SelectedAxes;
            if (selected.Count == 0) return;

            var origin = uiDocument.Selection.PickPoint(
                "Bấm điểm gốc đặt lưới (góc dưới-trái của vùng lưới CAD).");

            var lines = CadGridDirectLineBuilder.Build(selected, origin);
            var result = new CadGridCreationService().CreateFromLines(document, lines);
            ShowResult(result, viewModel.Preview.SkippedIds.Count);
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Create Grid from CAD command failed");
            TaskDialog.Show(Title, "Không thể tạo lưới từ CAD.\n\n" + exception.Message);
        }
    }

    private static CadGridTransferPackage? ChooseSource()
    {
        return PickFromAutoCad();
    }

    private static (CadGridTransferPackage Package, CadGridPreview Preview)? Reselect()
    {
        var package = PickFromAutoCad();
        if (package is null) return null;

        var preview = CadGridPreviewBuilder.Build(package);
        if (preview.IsValid) return (package, preview);

        TaskDialog.Show(Title, "Không đọc được lưới từ vùng chọn mới.\n\n" + preview.Error);
        return null;
    }

    private static CadGridTransferPackage? PickFromAutoCad()
    {
        var result = AutoCadSelectionService.SelectLines();
        if (result.IsValid) return result.Package;

        // An empty message means the user pressed Escape in AutoCAD.
        if (!string.IsNullOrWhiteSpace(result.Error))
            TaskDialog.Show(Title, result.Error);
        return null;
    }

    private static void ShowResult(CadGridCreationResult result, int skippedCount)
    {
        var content =
            $"Đã tạo: {result.CreatedIds.Count} Grid"
            + $"\nGrid đã tồn tại (bỏ qua): {result.ExistingCount}"
            + $"\nLine quá ngắn (bỏ qua): {skippedCount}"
            + $"\nLỗi tạo Grid: {result.Errors.Count}";

        if (result.Errors.Count > 0)
            content += "\n\n" + string.Join("\n", result.Errors.Take(3));

        TaskDialog.Show(Title, content);
    }

    private static bool IsSupportedPlan(ViewPlan viewPlan) =>
        viewPlan.ViewType is ViewType.FloorPlan
            or ViewType.CeilingPlan
            or ViewType.EngineeringPlan;
}
