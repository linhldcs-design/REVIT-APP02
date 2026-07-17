using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using FootingDrawing.Addin.Services;
using FootingDrawing.Addin.ViewModels;
using FootingDrawing.Addin.Views;
using FootingDrawing.Core.Models;
using Nice3point.Revit.Toolkit.External;
using Serilog;
using RevitAPP.Licensing;

namespace FootingDrawing.Addin.Commands;

/// <summary>
///     Lệnh triển khai bản vẽ mặt bằng thép móng: pick móng → mở dialog cấu hình → (Phase 5) sinh bản vẽ.
///     Self-contained: tự khởi tạo logger (khi nạp qua Add-in Manager, OnStartup không chạy).
/// </summary>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
public sealed class FootingDrawingCommand : ExternalCommand
{
    public override void Execute()
    {
        var (licenseOk, licenseMessage) = LicenseService.EnsureValid();
        if (!licenseOk) { TaskDialog.Show("Bản Vẽ Móng", licenseMessage); return; }

        Host.Start();
        var uiDocument = Application.ActiveUIDocument;
        if (uiDocument is null) return;
        var document = uiDocument.Document;

        try
        {
            // 1. Pick một hoặc nhiều móng.
            var footings = new FoundationPicker().PickFoundations(uiDocument);
            if (footings.Count == 0) return; // user huỷ

            // 2. Nạp nguồn dữ liệu + cấu hình qua dialog.
            var resources = new ProjectResourceProvider().LoadResources(document);
            var viewModel = new FootingDrawingViewModel(resources, new JsonSettingStore());
            var view = new FootingDrawingView(viewModel);
            new WindowInteropHelper(view) { Owner = Application.MainWindowHandle };

            if (view.ShowDialog() != true || viewModel.Result is null) return;

            // 3. Sinh bản vẽ cho từng móng đã chọn.
            var orchestrator = new FootingDrawingOrchestrator();
            var results = new List<FootingDrawingResult>();
            var errors = new List<string>();
            foreach (var footing in footings)
            {
                var mark = FoundationInfo.GetMark(footing);
                try { results.Add(orchestrator.Generate(document, footing, viewModel.Result)); }
                catch (Exception ex)
                {
                    Log.Error(ex, "Sinh bản vẽ móng {Mark} thất bại", mark);
                    errors.Add($"Móng '{mark}': {ex.Message}");
                }
            }

            ActivateSheet(uiDocument, document, results);

            var message = $"Đã tạo {results.Count}/{footings.Count} bản vẽ móng: " +
                          $"{results.Sum(r => r.TagCount)} tag, {results.Sum(r => r.BendingDetailCount)} bending detail, " +
                          $"{results.Sum(r => r.DimensionCount)} dimension.";
            var warnings = results.SelectMany(r => r.Warnings).Distinct().ToList();
            if (warnings.Count > 0) message += "\n\nCảnh báo:\n- " + string.Join("\n- ", warnings);
            if (errors.Count > 0) message += "\n\nLỗi:\n- " + string.Join("\n- ", errors);

            TaskDialog.Show("Bản Vẽ Móng", message);
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Bản Vẽ Móng thất bại");
            TaskDialog.Show("Bản Vẽ Móng", "Lỗi: " + exception.Message);
        }
    }

    private static void ActivateSheet(UIDocument uiDocument, Document document, IReadOnlyList<FootingDrawingResult> results)
    {
        var sheetId = results.LastOrDefault(r => r.SheetId != 0)?.SheetId;
        if (sheetId is null) return;
        if (document.GetElement(ElementIdCompat.FromLong(sheetId.Value)) is ViewSheet sheet)
            uiDocument.ActiveView = sheet;
    }
}
