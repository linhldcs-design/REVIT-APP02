using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BeamRebarPro.Models;
using BeamRebarPro.Services.Rebar;

namespace BeamRebarPro.Services;

/// <summary>
///     External event handler vẽ thép cho NHIỀU dầm trong MỘT lần raise, mỗi dầm dùng một
///     <see cref="QuickSettingModel"/> riêng (dầm chính/phụ khác cấu hình). Chặn caller bằng
///     <see cref="WaitForCompletion"/> tới khi vẽ xong toàn bộ → gọi tự động (MCP/script) không cần
///     raise từng dầm rồi sleep chờ idle. Mỗi cặp (dầm, model) gọi orchestrator riêng nên thép chủ
///     KHÔNG bị nối liền giữa các dầm khác nhau.
/// </summary>
public sealed class BatchRebarHandler : IExternalEventHandler
{
    private readonly System.Threading.ManualResetEvent _done = new(false);

    /// <summary>Danh sách cặp (dầm, cấu hình thép) cần vẽ tuần tự trong cùng một event.</summary>
    public IReadOnlyList<(FamilyInstance Beam, QuickSettingModel Model)> Jobs { get; set; } =
        new List<(FamilyInstance, QuickSettingModel)>();

    // Output
    public bool Success { get; private set; }
    public string Message { get; private set; } = "";

    /// <summary>Kết quả từng dầm: ElementId → số thanh thép tạo (longitudinal + đai + chống phình).</summary>
    public IReadOnlyDictionary<long, int> CreatedPerBeam => _createdPerBeam;

    private readonly Dictionary<long, int> _createdPerBeam = new();

    public void Execute(UIApplication app)
    {
        try
        {
            var document = app.ActiveUIDocument.Document;
            var orchestrator = new BeamRebarOrchestrator();
            var warnings = new List<string>();
            var totalBars = 0;

            foreach (var (beam, model) in Jobs)
            {
                if (beam is null || model is null) continue;

                // Orchestrator tự mở transaction cho từng dầm → không mở ở đây.
                var result = orchestrator.Create(document, new[] { beam }, model);
                var bars = result.LongitudinalCount + result.StirrupCount + result.AntiBulgeCount;
                _createdPerBeam[beam.Id.ToValue()] = bars;
                totalBars += bars;
                if (result.Warnings.Count > 0)
                    warnings.AddRange(result.Warnings);
            }

            Success = true;
            Message = $"Đã vẽ {totalBars} thanh thép cho {Jobs.Count} dầm.";
            if (warnings.Count > 0)
                Message += " | " + string.Join("; ", warnings.Distinct());
        }
        catch (Exception ex)
        {
            Success = false;
            Message = "Lỗi: " + ex.Message;
        }
        finally
        {
            _done.Set();
        }
    }

    public string GetName() => "BeamRebarPro - Batch vẽ thép dầm";

    /// <summary>Chặn tới khi Execute chạy xong (Revit về idle và xử lý event). Trả false nếu quá hạn.</summary>
    public bool WaitForCompletion(int timeoutMs) => _done.WaitOne(timeoutMs);
}
