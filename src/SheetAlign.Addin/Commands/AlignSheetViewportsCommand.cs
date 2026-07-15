using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Nice3point.Revit.Toolkit.External;
using SheetAlign.Addin.Models;
using SheetAlign.Addin.Services;
using SheetAlign.Addin.ViewModels;
using SheetAlign.Addin.Views;
using Serilog;

namespace SheetAlign.Addin.Commands;

[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
public class AlignSheetViewportsCommand : ExternalCommand
{
    public override void Execute()
    {
        LoggerSetup.EnsureConfigured();

        var document = Application.ActiveUIDocument.Document;
        var service = new SheetAlignmentService();

        var sheets = service.GetSheets(document);
        if (sheets.Count < 2)
        {
            TaskDialog.Show("SheetAlign", "Can it nhat 2 sheet de can chinh.");
            return;
        }

        var grids = service.GetGrids(document);
        if (grids.Count < 1)
        {
            TaskDialog.Show("SheetAlign", "Can it nhat 1 luoi truc (dang duong thang) trong mo hinh.");
            return;
        }

        var levels = service.GetLevels(document);

        var sheetItems = sheets
            .Select(sheet => new SheetItemViewModel(sheet.Id, sheet.SheetNumber, sheet.Name))
            .ToList();
        var gridOptions = grids
            .Select(grid => new GridOption(grid.Id, grid.Name))
            .ToList();
        var levelOptions = levels
            .Select(level => new LevelOption(level.Id, level.Name))
            .ToList();

        var viewModel = new SheetAlignViewModel(sheetItems, gridOptions, levelOptions);
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

            // Tính điểm neo model theo chế độ user chọn.
            XYZ? modelAnchor;
            if (viewModel.Mode == AnchorMode.GridLevel)
            {
                var level = (Level)document.GetElement(viewModel.SelectedLevel!.LevelId);
                modelAnchor = service.GetGridLevelAnchorModel(gridA, level);
            }
            else
            {
                var gridB = (Grid)document.GetElement(viewModel.GridB!.GridId);
                modelAnchor = service.GetGridIntersectionModel(gridA, gridB);
            }

            if (modelAnchor == null)
            {
                TaskDialog.Show("SheetAlign",
                    "Khong tinh duoc diem neo (2 truc song song hoac truc khong phai duong thang).");
                return;
            }

            var result = service.Apply(document, masterSheet, targetSheets, modelAnchor);
            ShowSummary(result);
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Align sheet viewports failed");
            TaskDialog.Show("SheetAlign", $"Loi khi can chinh: {exception.Message}");
        }
    }

    private static void ShowSummary(SheetAlignResult result)
    {
        var message = new StringBuilder();
        message.AppendLine($"Da can chinh {result.UpdatedCount} sheet.");

        if (result.Skipped.Count > 0)
        {
            message.AppendLine($"Bo qua {result.Skipped.Count} sheet:");
            foreach (var skip in result.Skipped)
            {
                message.AppendLine($"  - {skip.SheetName}: {skip.Reason}");
            }
        }

        TaskDialog.Show("SheetAlign", message.ToString());
    }
}
