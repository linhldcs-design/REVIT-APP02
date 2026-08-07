using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Nice3point.Revit.Toolkit.External;
using RevitAPP.Core.Models.CadStructure;
using RevitAPP.Services.CadGrid;
using RevitAPP.Services.CadStructure;
using RevitAPP.ViewModels;
using RevitAPP.Views;
using Serilog;

namespace RevitAPP.Commands;

[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
public sealed class ModelFromCadCommand : ExternalCommand
{
    private const string Title = "Model From CAD";

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
            || viewPlan.ViewType is not (ViewType.FloorPlan or ViewType.CeilingPlan or ViewType.EngineeringPlan))
        {
            TaskDialog.Show(Title, "Hãy mở một mặt bằng có Level trước khi chạy lệnh.");
            return;
        }

        try
        {
            var projectOptions = CadColumnProjectOptionsReader.Read(document);
            var viewModel = new ModelFromCadViewModel(
                CadModelPreviewFactory.Empty(), projectOptions, SelectAndBuildPreview,
                SelectAndBuildBeamPreview, SelectAndBuildSlabPreview);
            var window = new ModelFromCadWindow(viewModel);
            new WindowInteropHelper(window) { Owner = Application.MainWindowHandle };
            if (window.ShowDialog() != true) return;

            var targetAnchor = uiDocument.Selection.PickPoint(
                "Chọn điểm móc tương ứng trong Revit.");

            if (viewModel.SelectedMode == ModelFromCadMode.Grid)
            {
                var planned = CadGridDirectLineBuilder.Build(
                    viewModel.SelectedGridAxes,
                    targetAnchor,
                    viewModel.Data.AnchorPreviewMm,
                    viewModel.RotationDegrees);
                var result = new CadGridCreationService().CreateFromLines(document, planned);
                ShowGridResult(result);
                return;
            }

            if (viewModel.SelectedMode == ModelFromCadMode.Beam)
            {
                if (!viewModel.BeamSettingsValid
                    || viewModel.BeamData is null
                    || viewModel.SelectedBeamFamily is null
                    || viewModel.SelectedBeamLevel is null
                    || string.IsNullOrWhiteSpace(viewModel.SelectedBeamWidthParameter)
                    || string.IsNullOrWhiteSpace(viewModel.SelectedBeamHeightParameter))
                {
                    TaskDialog.Show(Title, "Thiết lập Family, b/h, Level hoặc Beam scan chưa hợp lệ.");
                    return;
                }

                var beamResult = CadBeamCreationService.Create(
                    document,
                    viewModel.SelectedBeams,
                    viewModel.BeamData.Analysis.SourceAnchorRelativeMm,
                    targetAnchor,
                    viewModel.RotationDegrees,
                    viewModel.SelectedBeamFamily,
                    viewModel.SelectedBeamWidthParameter!,
                    viewModel.SelectedBeamHeightParameter!,
                    viewModel.SelectedBeamLevel,
                    viewModel.BeamZOffsetMm);
                ShowBeamResult(beamResult);
                return;
            }

            if (viewModel.SelectedMode == ModelFromCadMode.Slab)
            {
                if (!viewModel.SlabSettingsValid
                    || viewModel.SlabData is null
                    || viewModel.SelectedSlabType is null
                    || viewModel.SelectedSlabLevel is null)
                {
                    TaskDialog.Show(Title, "Thiết lập Floor Type, Level hoặc Slab scan chưa hợp lệ.");
                    return;
                }

                var slabResult = CadSlabCreationService.Create(
                    document,
                    viewModel.SelectedSlabs,
                    viewModel.SlabData.Analysis.SourceAnchorMm,
                    targetAnchor,
                    viewModel.RotationDegrees,
                    viewModel.SelectedSlabType,
                    viewModel.SelectedSlabLevel);
                ShowSlabResult(slabResult);
                return;
            }

            if (!viewModel.ColumnSettingsValid
                || viewModel.SelectedFamily is null
                || viewModel.SelectedBaseLevel is null
                || viewModel.SelectedTopLevel is null
                || string.IsNullOrWhiteSpace(viewModel.SelectedWidthParameter)
                || string.IsNullOrWhiteSpace(viewModel.SelectedHeightParameter))
            {
                TaskDialog.Show(Title, "Thiết lập Family, b/h hoặc Level của tab Column chưa hợp lệ.");
                return;
            }

            var widthParameter = viewModel.SelectedWidthParameter!;
            var heightParameter = viewModel.SelectedHeightParameter!;

            var columnResult = CadColumnCreationService.Create(
                document,
                viewModel.SelectedColumns,
                viewModel.Data.AnchorPreviewMm,
                targetAnchor,
                viewModel.RotationDegrees,
                viewModel.SelectedFamily,
                widthParameter,
                heightParameter,
                viewModel.SelectedBaseLevel,
                viewModel.SelectedTopLevel,
                viewModel.BaseOffsetMm,
                viewModel.TopOffsetMm);
            ShowColumnResult(columnResult);
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Model From CAD command failed");
            TaskDialog.Show(Title, "Không thể tạo model từ CAD.\n\n" + exception.Message);
        }
    }

    private static CadModelPreviewData? SelectAndBuildPreview()
    {
        var selection = AutoCadModelSelectionService.Select();
        if (!selection.IsValid)
        {
            if (!string.IsNullOrWhiteSpace(selection.Error)) TaskDialog.Show(Title, selection.Error);
            return null;
        }

        var preview = CadModelPreviewFactory.Build(selection.Package!, out var error);
        if (preview is null)
            TaskDialog.Show(Title, "Không phân tích được vùng chọn CAD.\n\n" + error);
        return preview;
    }

    private static CadBeamPreviewData? SelectAndBuildBeamPreview(
        CadStructureTransferPackage gridPackage,
        CadBeamAnalysisOptions options)
    {
        var preview = CadBeamPreviewFactory.SelectAndBuild(gridPackage, options, out var error);
        if (preview is null && !string.IsNullOrWhiteSpace(error)) TaskDialog.Show(Title, error);
        return preview;
    }

    private static void ShowGridResult(CadGridCreationResult result)
    {
        var message = $"Đã tạo: {result.CreatedIds.Count} Grid"
                      + $"\nGrid đã tồn tại: {result.ExistingCount}"
                      + $"\nLỗi: {result.Errors.Count}";
        if (result.Errors.Count > 0) message += "\n\n" + string.Join("\n", result.Errors.Take(3));
        TaskDialog.Show(Title, message);
    }

    private static void ShowColumnResult(CadColumnCreationResult result)
    {
        var message = $"Đã tạo: {result.CreatedIds.Count} Column"
                      + $"\nColumn đã tồn tại: {result.ExistingCount}"
                      + $"\nLỗi: {result.Errors.Count}";
        if (result.Errors.Count > 0) message += "\n\n" + string.Join("\n", result.Errors.Take(3));
        TaskDialog.Show(Title, message);
    }

    private static void ShowBeamResult(CadBeamCreationResult result)
    {
        var message = $"Đã tạo: {result.CreatedIds.Count} Beam"
                      + $"\nBeam đã tồn tại: {result.ExistingCount}"
                      + $"\nLỗi: {result.Errors.Count}";
        if (result.Errors.Count > 0) message += "\n\n" + string.Join("\n", result.Errors.Take(3));
        TaskDialog.Show(Title, message);
    }

    private static CadSlabPreviewData? SelectAndBuildSlabPreview(
        CadStructureTransferPackage gridPackage,
        CadSlabAnalysisOptions options)
    {
        var preview = CadSlabPreviewFactory.SelectAndBuild(gridPackage, options, out var error);
        if (preview is null && !string.IsNullOrWhiteSpace(error)) TaskDialog.Show(Title, error);
        return preview;
    }

    private static void ShowSlabResult(CadSlabCreationResult result)
    {
        var message = $"Đã tạo: {result.CreatedIds.Count} Sàn"
                      + $"\nSàn đã tồn tại: {result.ExistingCount}"
                      + $"\nLỗi: {result.Errors.Count}";
        if (result.Errors.Count > 0) message += "\n\n" + string.Join("\n", result.Errors.Take(3));
        TaskDialog.Show(Title, message);
    }
}
