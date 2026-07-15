using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using BeamRebar.Addin.Services;
using BeamRebar.Addin.Services.Rebar;
using BeamRebar.Addin.ViewModels;
using BeamRebar.Addin.Views;
using Nice3point.Revit.Toolkit.External;
using Serilog;

namespace BeamRebar.Addin.Commands;

/// <summary>
///     Tạo cốt thép 3D cho dầm BTCT theo TCVN 5574: chọn dầm (1 nhịp hoặc nhiều nhịp liên tục) →
///     cấu hình qua Quick Setting → sinh Rebar. Self-contained: tự khởi tạo logger trong Execute()
///     vì khi nạp qua Add-in Manager, Application.OnStartup KHÔNG chạy.
/// </summary>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
public class BeamRebarCommand : ExternalCommand
{
    public override void Execute()
    {
        LoggerSetup.EnsureConfigured();
        var uiDocument = Application.ActiveUIDocument;
        var document = uiDocument.Document;

        // 1. Pick dầm (1 hoặc nhiều nhịp).
        var beams = new BeamPicker().PickBeams(uiDocument, out var pickError);
        if (beams.Count == 0)
        {
            if (!string.IsNullOrEmpty(pickError)) TaskDialog.Show("Beam Rebar", pickError);
            return;
        }

        // 2. Cấu hình qua Quick Setting.
        var viewModel = new QuickSettingViewModel();
        var view = new QuickSettingView(viewModel);
        new WindowInteropHelper(view) { Owner = Application.MainWindowHandle };

        if (view.ShowDialog() != true || viewModel.Result == null) return;

        // Cảnh báo an toàn: tạo thép cho nhiều dầm cùng lúc nặng + dễ lỗi geometry hàng loạt.
        if (beams.Count > 5)
        {
            var confirm = TaskDialog.Show("Beam Rebar",
                $"Bạn đang tạo thép cho {beams.Count} dầm cùng lúc. Nên thử trước trên 1 dầm để kiểm tra. Tiếp tục?",
                TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No);
            if (confirm != TaskDialogResult.Yes) return;
        }

        // 3. Tạo thép.
        try
        {
            var result = new BeamRebarOrchestrator().Create(document, beams, viewModel.Result);

            var message = $"Đã tạo {result.Total} cấu kiện thép cho {beams.Count} dầm " +
                          $"({result.LongitudinalCount} thép dọc, {result.StirrupCount} đai, " +
                          $"{result.AntiBulgeCount} chống phình).";
            if (result.Warnings.Count > 0)
                message += "\n\nCảnh báo:\n- " + string.Join("\n- ", result.Warnings.Distinct().Take(10));

            TaskDialog.Show("Beam Rebar", message);
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Tạo thép dầm thất bại");
            TaskDialog.Show("Beam Rebar", "Lỗi khi tạo thép: " + exception.Message);
        }
    }
}
