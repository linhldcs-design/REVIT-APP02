using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Nice3point.Revit.Toolkit.External;
using RevitAPP.Services.SheetAlign;
using RevitAPP.ViewModels;
using RevitAPP.Views;
using Serilog;

namespace RevitAPP.Commands
{
    [UsedImplicitly]
    [Transaction(TransactionMode.Manual)]
    public class AlignSheetViewportsCommand : ExternalCommand
    {
        public override void Execute()
        {
            if (!LicenseCommandGate.Ensure("Căn Chỉnh View")) return;
            var document = Application.ActiveUIDocument.Document;
            var service = new SheetAlignmentService();

            var sheets = service.GetSheets(document);
            if (sheets.Count < 2)
            {
                TaskDialog.Show("RevitAI", "Cần ít nhất 2 sheet để căn chỉnh.");
                return;
            }

            var grids = service.GetGrids(document);
            if (grids.Count < 2)
            {
                TaskDialog.Show("RevitAI", "Cần ít nhất 2 lưới trục (dạng đường thẳng) trong mô hình.");
                return;
            }

            var sheetItems = sheets
                .Select(sheet => new SheetItemViewModel(sheet.Id, sheet.SheetNumber, sheet.Name))
                .ToList();
            var gridOptions = grids
                .Select(grid => new GridOption(grid.Id, grid.Name))
                .ToList();

            var viewModel = new SheetAlignViewModel(sheetItems, gridOptions);
            var window = new SheetAlignWindow(Application.MainWindowHandle, viewModel);
            if (window.ShowDialog() != true)
            {
                return;
            }

            try
            {
                var masterSheet = (ViewSheet)document.GetElement(viewModel.SelectedMaster!.SheetId);
                var targetSheets = viewModel.SelectedSheets
                    .Select(item => (ViewSheet)document.GetElement(item.SheetId))
                    .ToList();
                var gridA = (Grid)document.GetElement(viewModel.GridA!.GridId);
                var gridB = (Grid)document.GetElement(viewModel.GridB!.GridId);

                var result = service.Apply(document, masterSheet, targetSheets, gridA, gridB);
                ShowSummary(result);
            }
            catch (Exception exception)
            {
                Log.Error(exception, "Align sheet viewports failed");
                TaskDialog.Show("RevitAI", $"Lỗi khi căn chỉnh: {exception.Message}");
            }
        }

        private static void ShowSummary(Core.Models.SheetAlignResult result)
        {
            var message = new StringBuilder();
            message.AppendLine($"Đã căn chỉnh {result.UpdatedCount} sheet.");

            if (result.Skipped.Count > 0)
            {
                message.AppendLine($"Bỏ qua {result.Skipped.Count} sheet:");
                foreach (var skip in result.Skipped)
                {
                    message.AppendLine($"  • {skip.SheetName}: {skip.Reason}");
                }
            }

            TaskDialog.Show("RevitAI", message.ToString());
        }
    }
}
