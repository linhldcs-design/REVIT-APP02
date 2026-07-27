using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using Nice3point.Revit.Toolkit.External;
using RevitAPP.Core.Models.BeamLongitudinalDrawing;
using RevitAPP.Core.Services;
using RevitAPP.Helpers;
using RevitAPP.Services.BeamDrawing;
using RevitAPP.Services.BeamLongitudinalDrawing;
using RevitAPP.ViewModels;
using RevitAPP.Views;

namespace RevitAPP.Commands;

/// <summary>Chọn chuỗi dầm đã có Rebar, review trục/station và cấu hình tài nguyên. Phase 02 không sửa model.</summary>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
public sealed class BeamLongitudinalDrawingCommand : ExternalCommand
{
    public override void Execute()
    {
        if (!LicenseCommandGate.Ensure("Mặt Cắt Dọc Dầm")) return;
        var uiDocument = Application.ActiveUIDocument;
        var validationError = BeamLongitudinalCommandContextValidator.Validate(uiDocument);
        if (validationError != null) { TaskDialog.Show("Mặt Cắt Dọc Dầm", validationError); return; }

        var document = uiDocument.Document;
        var beams = new BeamPicker().PickBeams(uiDocument, out var pickError);
        if (beams.Count == 0)
        {
            if (!string.IsNullOrWhiteSpace(pickError)) TaskDialog.Show("Mặt Cắt Dọc Dầm", pickError);
            return;
        }
        if (beams.Count != 1)
        {
            TaskDialog.Show("Mặt Cắt Dọc Dầm", "Chỉ chọn một cây dầm chạy xuyên qua nhiều cột.");
            return;
        }
        if (!new LongitudinalBeamSelectionReader().TryRead(document, beams, out var spans, out var profiles, out var readError))
        { TaskDialog.Show("Mặt Cắt Dọc Dầm", readError); return; }

        var chainResult = BeamChainBuilder.Build(spans, BeamChainTolerance.Default);
        if (!chainResult.IsValid || chainResult.Model == null)
        {
            TaskDialog.Show("Mặt Cắt Dọc Dầm", string.Join("\n", chainResult.Errors.Select(e => e.Message)));
            return;
        }

        var resources = new LongitudinalProjectResourceProvider().Load(document);
        var viewModel = new BeamLongitudinalDrawingViewModel(
            resources, spans, profiles, chainResult.Model);
        var window = new BeamLongitudinalDrawingWindow(viewModel);
        new WindowInteropHelper(window) { Owner = Application.MainWindowHandle };
        if (window.ShowDialog() == true && viewModel.Result != null)
        {
            try
            {
                var result = new LongitudinalDrawingOrchestrator().Generate(document, beams, viewModel.Result);
                var message =
                    $"Đã tạo {result.LongitudinalViewIds.Count} mặt cắt dọc và {result.CrossSectionViewIds.Count} mặt cắt ngang.";
                if (result.Warnings.Count > 0)
                    message += "\n\nCảnh báo/chẩn đoán:\n- " +
                               string.Join("\n- ", result.Warnings.Select(warning => warning.Message).Distinct());
                TaskDialog.Show("Mặt Cắt Dọc Dầm", message);
                if (result.SheetId is { } sheetId &&
                    document.GetElement(ElementIdHelper.Create(sheetId)) is Autodesk.Revit.DB.ViewSheet sheet)
                    uiDocument.ActiveView = sheet;
            }
            catch (Exception exception)
            {
                TaskDialog.Show("Mặt Cắt Dọc Dầm", $"Không thể tạo view; mọi thay đổi đã rollback.\n{exception.Message}");
            }
        }
    }

}
