using RevitAPP.Helpers;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using Nice3point.Revit.Toolkit.External;
using RevitAPP.Services.BeamDrawing;
using RevitAPP.ViewModels;
using RevitAPP.Views;
using Serilog;

namespace RevitAPP.Commands;

/// <summary>
///     Triển khai bản vẽ dầm: pick dầm (đã có Rebar sẵn) → cấu hình → sinh Sectional Elevation +
///     Cross Section (+ annotation Phase 5) → đặt lên 1 sheet. Feature native trong RevitAPP.
///     Self-contained để chạy được qua Add-In Manager (OnStartup KHÔNG chạy khi nạp kiểu đó).
/// </summary>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
public class BeamDrawingCommand : ExternalCommand
{
    public override void Execute()
    {
        if (!LicenseCommandGate.Ensure("Bản Vẽ Dầm")) return;
        var uiDocument = Application.ActiveUIDocument;
        var document = uiDocument.Document;

        // 1. Pick dầm.
        var beams = new BeamPicker().PickBeams(uiDocument, out var pickError);
        if (beams.Count == 0)
        {
            if (!string.IsNullOrEmpty(pickError)) TaskDialog.Show("Ban Ve Dam", pickError);
            return;
        }

        // 2. Cấu hình qua dialog.
        var resources = new ProjectResourceProvider().LoadResources(document);
        var viewModel = new BeamDrawingViewModel(resources);
        TryLoadPreview(document, beams[0], viewModel);
        var window = new BeamDrawingWindow(viewModel);
        new WindowInteropHelper(window) { Owner = Application.MainWindowHandle };

        if (window.ShowDialog() != true || viewModel.Result == null) return;

        // 3. Cảnh báo family thiếu (không chặn).
        var setting = viewModel.Result;
        var familyWarnings = new RequiredFamilyValidator().FindMissing(document, setting);

        // 4. Sinh bản vẽ (kèm annotation qua BeamAnnotator ở T2).
        try
        {
            var orchestrator = new BeamDrawingOrchestrator { Annotator = new BeamAnnotator() };
            var result = orchestrator.Generate(document, beams, setting);
            foreach (var w in familyWarnings) result.Warnings.Add(w);

            var message = $"Đã tạo {result.TotalViews} view ({result.SectionViewIds.Count} mặt cắt dọc, " +
                          $"{result.CrossSectionViewIds.Count} mặt cắt ngang) cho {beams.Count} dầm.";
            if (result.Warnings.Count > 0)
                message += "\n\nCảnh báo:\n- " + string.Join("\n- ", result.Warnings.Distinct());

            TaskDialog.Show("Ban Ve Dam", message);

            // Mở tới sheet chứa view vừa đặt (nhảy active view sang sheet).
            if (result.SheetId is { } sheetIdValue)
            {
                var sheet = document.GetElement(ElementIdHelper.Create(sheetIdValue)) as Autodesk.Revit.DB.View;
                if (sheet != null) uiDocument.ActiveView = sheet;
            }
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Tao ban ve dam that bai");
            TaskDialog.Show("Ban Ve Dam", "Lỗi khi tạo bản vẽ: " + exception.Message);
        }
    }

    /// <summary>Đọc tiết diện thật dầm đầu tiên (GỐI + NHỊP) để dialog vẽ sơ đồ preview sát thực tế.</summary>
    private static void TryLoadPreview(Autodesk.Revit.DB.Document document,
        Autodesk.Revit.DB.FamilyInstance beam, BeamDrawingViewModel viewModel)
    {
        try
        {
            if (!new BeamGeometryReader().TryRead(document, beam, out var geometry, out _)) return;
            var (supportT, midSpanT) = new BeamSupportFinder().FindStations(document, beam, geometry);
            var reader = new CrossSectionPreviewReader();
            viewModel.SupportPreview = reader.Read(document, beam, supportT);
            viewModel.MidSpanPreview = reader.Read(document, beam, midSpanT);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Khong doc duoc preview tiet dien dam");
        }
    }
}
