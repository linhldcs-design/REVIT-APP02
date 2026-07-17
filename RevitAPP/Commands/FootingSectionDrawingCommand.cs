using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Nice3point.Revit.Toolkit.External;
using RevitAPP.Helpers;
using RevitAPP.Services.BeamDrawing;
using RevitAPP.Services.FootingSection;
using RevitAPP.ViewModels;
using RevitAPP.Views;
using Serilog;

namespace RevitAPP.Commands;

/// <summary>
///     Triển khai mặt cắt móng: pick móng (đã có Rebar sẵn) → cấu hình → sinh section đứng qua móng+cổ+cột
///     (+ tag / dim / cao độ) → đặt lên 1 sheet. Feature native trong RevitAPP, self-contained để chạy qua
///     Add-In Manager (OnStartup KHÔNG chạy khi nạp kiểu đó).
/// </summary>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
public class FootingSectionDrawingCommand : ExternalCommand
{
    public override void Execute()
    {
        if (!LicenseCommandGate.Ensure("Mặt Cắt Móng")) return;
        var uiDocument = Application.ActiveUIDocument;
        var document = uiDocument.Document;

        // 1. Pick móng.
        var footing = new FootingPicker().PickFooting(uiDocument, out var pickError);
        if (footing == null)
        {
            if (!string.IsNullOrEmpty(pickError)) TaskDialog.Show("Mat Cat Mong", pickError);
            return;
        }

        // 2. Cấu hình qua dialog, bao gồm hướng cắt X/Y.
        var resources = new ProjectResourceProvider().LoadResources(document);
        var levelNames = new FilteredElementCollector(document)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .OrderBy(level => level.Elevation)
            .Select(level => level.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var viewModel = new FootingSectionViewModel(resources, levelNames: levelNames);
        var window = new FootingSectionWindow(viewModel);
        new WindowInteropHelper(window) { Owner = Application.MainWindowHandle };

        if (window.ShowDialog() != true || viewModel.Result == null) return;

        // 3. Đọc geometry theo hướng người dùng đã chọn.
        if (!new FootingGeometryReader().TryRead(document, footing, viewModel.Result.Direction,
                viewModel.Result.ViewBottomLevelName, viewModel.Result.ViewTopLevelName,
                out var geometry, out var geoError))
        {
            TaskDialog.Show("Mat Cat Mong", geoError);
            return;
        }

        // 4. Sinh mặt cắt (kèm annotation).
        try
        {
            var orchestrator = new FootingSectionOrchestrator { Annotator = new FootingSectionAnnotator() };
            var result = orchestrator.Generate(document, footing, geometry, viewModel.Result);

            var message = $"Đã tạo mặt cắt móng {geometry.Mark}.";
            if (result.Warnings.Count > 0)
                message += "\n\nCảnh báo:\n- " + string.Join("\n- ", result.Warnings.Distinct());
            TaskDialog.Show("Mat Cat Mong", message);

            // Mở tới sheet chứa view vừa đặt.
            if (result.SheetId is { } sheetIdValue)
            {
                var sheet = document.GetElement(ElementIdHelper.Create(sheetIdValue)) as Autodesk.Revit.DB.View;
                if (sheet != null) uiDocument.ActiveView = sheet;
            }
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Tao mat cat mong that bai");
            TaskDialog.Show("Mat Cat Mong", "Lỗi khi tạo bản vẽ: " + exception.Message);
        }
    }
}
