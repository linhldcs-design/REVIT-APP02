using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Nice3point.Revit.Toolkit.External;
using RevitAPP.Licensing;
using RevitAPP.Services.ColumnRebar;
using RevitAPP.ViewModels;
using RevitAPP.Views;
using Serilog;

namespace RevitAPP.Commands;

/// <summary>
///     Vẽ cốt thép cột (thép chủ + thép đai 3 vùng) theo cấu tạo TCVN.
///     Pick cột → phát hiện hệ cột thẳng hàng → cấu hình per-tầng → sinh Rebar trong 1 Transaction.
/// </summary>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
public class DrawColumnRebarCommand : ExternalCommand
{
    public override void Execute()
    {
        var (licenseOk, licenseMsg) = LicenseService.EnsureValid();
        if (!licenseOk) { TaskDialog.Show("Vẽ Thép Cột", licenseMsg); return; }

        var uiDocument = Application.ActiveUIDocument;
        var document = uiDocument.Document;

        var detector = new ColumnStackDetector();
        var columns = detector.PickColumns(uiDocument, out var pickError);
        if (columns.Count == 0)
        {
            if (!string.IsNullOrEmpty(pickError)) TaskDialog.Show("Vẽ Thép Cột", pickError);
            return;
        }

        // 1 cột → tự dò cả hệ thẳng hàng; nhiều cột → dùng ĐÚNG các cột đã chọn (linh hoạt từng tầng).
        var stack = columns.Count == 1
            ? detector.BuildStack(document, columns[0], out var buildError)
            : detector.BuildStackFromColumns(document, columns, out buildError);
        if (stack.Count == 0)
        {
            TaskDialog.Show("Vẽ Thép Cột", string.IsNullOrEmpty(buildError) ? "Không tìm thấy cột hợp lệ." : buildError);
            return;
        }

        var barTypes = new RebarBarTypeProvider().GetAll(document);
        if (barTypes.Count == 0)
        {
            TaskDialog.Show("Vẽ Thép Cột", "Dự án chưa có loại thanh thép (RebarBarType). Hãy nạp Rebar Bar Type trước.");
            return;
        }

        var configStore = new ColumnRebarConfigStore();
        var viewModel = new ColumnRebarViewModel(stack, barTypes, configStore.LoadAll(document))
        {
            SavePresetCallback = config => configStore.Save(document, config),
            DeletePresetCallback = name => configStore.Delete(document, name)
        };
        var view = new ColumnRebarView(viewModel);
        new WindowInteropHelper(view) { Owner = Application.MainWindowHandle };

        if (view.ShowDialog() != true || viewModel.Result == null) return;

        try
        {
            using var transaction = new Transaction(document, "Vẽ thép cột");
            transaction.Start();
            var result = new ColumnRebarBuilder().Build(
                document, stack, viewModel.Result, viewModel.LapOptions, viewModel.FoundationOptions,
                viewModel.StirrupSpreadOptions, viewModel.ColumnEndOptions, viewModel.AddPartition,
                viewModel.SectionTransitionOptions);
            transaction.Commit();

            var message = $"Đã tạo {result.MainBarCount} thanh thép chủ và {result.StirrupSetCount} bộ đai cho {viewModel.Result.Count} tầng.";
            if (result.StarterBarCount > 0)
                message += $"\nThép chờ móng: {result.StarterBarCount} thanh.";
            if (result.Warnings.Count > 0)
                message += "\n\nCảnh báo:\n- " + string.Join("\n- ", result.Warnings);

            TaskDialog.Show("Vẽ Thép Cột", message);
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Vẽ thép cột thất bại");
            TaskDialog.Show("Vẽ Thép Cột", "Lỗi khi tạo cốt thép: " + exception.Message);
        }
    }
}
